using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class PrintLoggingService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PrintLoggingService(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogPrintAsync(int tenantId, int shopId, int bookId, int copies, string? userId, string? userName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shop = await db.Shops.FindAsync(shopId);
        var book = await db.Books.FindAsync(bookId);

        if (shop == null || book == null) return;

        var log = new PrintLog
        {
            TenantId = tenantId,
            ShopId = shopId,
            BookId = bookId,
            ShopName = shop.Name,
            BookTitle = book.Title,
            Copies = copies,
            PrintedByUserId = userId,
            PrintedByUserName = userName,
            IPAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            PrintedAt = DateTime.UtcNow
        };

        db.PrintLogs.Add(log);
        await db.SaveChangesAsync();
    }

    public async Task<List<PrintLog>> GetRecentLogsAsync(int count = 50)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .OrderByDescending(l => l.PrintedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetTotalPrintsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs.SumAsync(l => l.Copies);
    }

    public async Task<Dictionary<string, int>> GetPrintsPerShopAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .GroupBy(l => l.ShopName)
            .Select(g => new { Shop = g.Key, Total = g.Sum(l => l.Copies) })
            .ToDictionaryAsync(x => x.Shop, x => x.Total);
    }

    public async Task<List<ShopPrintStat>> GetShopPrintStatsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .GroupBy(l => new { l.ShopId, l.ShopName })
            .Select(g => new ShopPrintStat
            {
                ShopId = g.Key.ShopId,
                ShopName = g.Key.ShopName,
                Total = g.Sum(l => l.Copies)
            })
            .OrderByDescending(s => s.Total)
            .ToListAsync();
    }

    public record ShopPrintStat
    {
        public int ShopId { get; init; }
        public string ShopName { get; init; } = "";
        public int Total { get; init; }
    }

    public async Task<Dictionary<string, int>> GetPrintsPerBookAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .GroupBy(l => l.BookTitle)
            .Select(g => new { Book = g.Key, Total = g.Sum(l => l.Copies) })
            .ToDictionaryAsync(x => x.Book, x => x.Total);
    }

    public async Task<List<PrintLog>> GetShopLogsAsync(int shopId, int count = 100)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PrintLogs
            .Where(l => l.ShopId == shopId)
            .OrderByDescending(l => l.PrintedAt)
            .Take(count)
            .ToListAsync();
    }
}