using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Controllers;
using System.Security.Cryptography;
using System.Text;

namespace PrintingBooksPortal.Services;

public interface IPrinterRegistrationService
{
    Task RegisterPrintersAsync(string apiKey, int? shopId, List<AgentPrinterInfo> printers);
    Task<List<RegisteredPrinter>> GetTenantPrintersAsync(int tenantId);
    Task CleanupOfflinePrintersAsync();
}

public class PrinterRegistrationService : IPrinterRegistrationService
{
    private readonly AppDbContext _db;
    private readonly IApiKeyService _apiKeys;
    private readonly ILogger<PrinterRegistrationService> _logger;

    public PrinterRegistrationService(AppDbContext db, IApiKeyService apiKeys, ILogger<PrinterRegistrationService> logger)
    {
        _db = db;
        _apiKeys = apiKeys;
        _logger = logger;
    }

    public async Task RegisterPrintersAsync(string apiKey, int? shopId, List<AgentPrinterInfo> printers)
    {
        var tenantId = await _apiKeys.ResolveTenantAsync(apiKey);
        if (tenantId == 0)
            return;

        var agentKeyHash = HashKey(apiKey);
        var now = DateTime.UtcNow;

        // Get existing printers for this agent (tenant + shop + key = exactly one agent)
        var existingPrinters = await _db.RegisteredPrinters
            .Where(p => p.TenantId == tenantId && p.ShopId == shopId && p.AgentKeyHash == agentKeyHash)
            .ToListAsync();

        foreach (var printer in printers)
        {
            var existing = existingPrinters.FirstOrDefault(p => p.Name == printer.Name);

            if (existing != null)
            {
                // Update existing printer
                existing.Port = printer.Port ?? "";
                existing.ConnectionType = printer.ConnectionType ?? "";
                existing.Driver = printer.Driver ?? "";
                existing.Location = printer.Location ?? "";
                existing.Comment = printer.Comment ?? "";
                existing.IsDefault = printer.IsDefault;
                existing.IsOnline = printer.IsOnline;
                existing.Status = printer.Status ?? "Unknown";
                existing.LastSeen = now;
                existing.UpdatedAt = now;
            }
            else
            {
                // Add new printer
                _db.RegisteredPrinters.Add(new RegisteredPrinter
                {
                    TenantId = tenantId,
                    ShopId = shopId,
                    Name = printer.Name,
                    Port = printer.Port ?? "",
                    ConnectionType = printer.ConnectionType ?? "",
                    Driver = printer.Driver ?? "",
                    Location = printer.Location ?? "",
                    Comment = printer.Comment ?? "",
                    IsDefault = printer.IsDefault,
                    IsOnline = printer.IsOnline,
                    Status = printer.Status ?? "Unknown",
                    AgentKeyHash = agentKeyHash,
                    LastSeen = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        // Mark printers as offline if they're no longer reported by this agent
        var reportedPrinterNames = printers.Select(p => p.Name).ToHashSet();
        var offlinePrinters = existingPrinters.Where(p => !reportedPrinterNames.Contains(p.Name));

        foreach (var offlinePrinter in offlinePrinters)
        {
            offlinePrinter.IsOnline = false;
            offlinePrinter.Status = "Offline";
            offlinePrinter.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated {Count} printers for tenant {TenantId} shop {ShopId}", printers.Count, tenantId, shopId);
    }

    public async Task<List<RegisteredPrinter>> GetTenantPrintersAsync(int tenantId)
    {
        return await _db.RegisteredPrinters
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task CleanupOfflinePrintersAsync()
    {
        // Mark printers as offline if they haven't been seen in the last 2 minutes
        var cutoffTime = DateTime.UtcNow.AddMinutes(-2);

        var stalePrinters = await _db.RegisteredPrinters
            .Where(p => p.LastSeen < cutoffTime && p.IsOnline)
            .ToListAsync();

        foreach (var printer in stalePrinters)
        {
            printer.IsOnline = false;
            printer.Status = "Offline";
            printer.UpdatedAt = DateTime.UtcNow;
        }

        if (stalePrinters.Any())
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Marked {Count} printers as offline due to inactivity", stalePrinters.Count);
        }
    }

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}