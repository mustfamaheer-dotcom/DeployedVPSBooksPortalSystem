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
    Task<List<RegisteredPrinter>> GetCurrentPrintersAsync(int tenantId, int? shopId);
    Task<bool?> HasPrinterAsync(string? printerName, int tenantId, int? shopId);
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

        // Atomic upsert (MERGE with HOLDLOCK): the old load-then-add approach raced —
        // two concurrent heartbeats both saw "no existing row", both INSERTed, and the
        // second hit the unique index, throwing every 3s and freezing LastSeen forever.
        foreach (var p in printers)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
MERGE dbo.RegisteredPrinters WITH (HOLDLOCK) AS t
USING (VALUES ({tenantId}, {shopId}, {agentKeyHash}, {p.Name})) AS s (TenantId, ShopId, AgentKeyHash, Name)
ON t.TenantId = s.TenantId AND t.ShopId = s.ShopId AND t.AgentKeyHash = s.AgentKeyHash AND t.Name = s.Name
WHEN MATCHED THEN UPDATE SET
    t.Port = {p.Port ?? ""},
    t.ConnectionType = {p.ConnectionType ?? ""},
    t.Driver = {p.Driver ?? ""},
    t.Location = {p.Location ?? ""},
    t.Comment = {p.Comment ?? ""},
    t.IsDefault = {p.IsDefault},
    t.IsOnline = {p.IsOnline},
    t.Status = {p.Status ?? "Unknown"},
    t.LastSeen = {now},
    t.UpdatedAt = {now}
WHEN NOT MATCHED THEN INSERT (TenantId, ShopId, Name, Port, ConnectionType, Driver, Location, Comment, IsDefault, IsOnline, Status, AgentKeyHash, LastSeen, CreatedAt, UpdatedAt)
VALUES (s.TenantId, s.ShopId, s.Name, {p.Port ?? ""}, {p.ConnectionType ?? ""}, {p.Driver ?? ""}, {p.Location ?? ""}, {p.Comment ?? ""}, {p.IsDefault}, {p.IsOnline}, {p.Status ?? "Unknown"}, s.AgentKeyHash, {now}, {now}, {now});");
        }

        _logger.LogInformation("Upserted {Count} printers for tenant {TenantId} shop {ShopId}", printers.Count, tenantId, shopId);
    }

    public async Task<List<RegisteredPrinter>> GetTenantPrintersAsync(int tenantId)
    {
        return await _db.RegisteredPrinters
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    /// <summary>
    /// The printers the agent(s) in scope read RIGHT NOW, straight from the database.
    /// An agent heartbeats every few seconds, refreshing LastSeen, so any row seen
    /// within the last 60 seconds is the agent's current read — unlike an in-memory
    /// cache, this survives restarts and is identical on every app instance.
    /// </summary>
    public async Task<List<RegisteredPrinter>> GetCurrentPrintersAsync(int tenantId, int? shopId)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        var query = _db.RegisteredPrinters
            .Where(p => p.TenantId == tenantId && p.LastSeen >= cutoff);

        if (shopId.HasValue)
            query = query.Where(p => p.ShopId == shopId);

        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    /// <summary>
    /// Accuracy gate backed by the database: true when the printer name is currently
    /// reported by an agent of the given scope, false when the scope has agents but
    /// none of them reads that printer, null when the scope has no current agent
    /// (nothing to validate against).
    /// </summary>
    public async Task<bool?> HasPrinterAsync(string? printerName, int tenantId, int? shopId)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return true;

        var current = await GetCurrentPrintersAsync(tenantId, shopId);
        if (current.Count == 0)
            return null;

        return current.Any(p =>
            string.Equals(p.Name?.Trim(), printerName.Trim(), StringComparison.OrdinalIgnoreCase));
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