using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

// ── DTOs (shapes shared by SystemAdminController API and SystemAdmin pages) ──

public class TeacherRow
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string? ownerName { get; set; }
    public string? contactEmail { get; set; }
    public string? phone { get; set; }
    public bool isActive { get; set; }
    public DateTime createdAt { get; set; }
    public string? plan { get; set; }
    public int? maxShops { get; set; }
    public int? maxBooks { get; set; }
    public TeacherRowStats stats { get; set; } = new();
}

public class TeacherRowStats
{
    public int shops { get; set; }
    public int books { get; set; }
    public int boards { get; set; }
    public int prints { get; set; }
}

public class CreateTeacherData
{
    public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Password { get; set; } = string.Empty;
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    public string? Plan { get; set; }
}

public class UpdateTeacherData
{
    public string? Name { get; set; }
    public string? OwnerName { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    public string? Plan { get; set; }
}

public class SaTenantProfile
{
    public int id { get; set; }
    public string name { get; set; } = string.Empty;
    public string? ownerName { get; set; }
    public string? contactEmail { get; set; }
    public string? phone { get; set; }
    public bool isActive { get; set; }
    public DateTime createdAt { get; set; }
    public string? plan { get; set; }
}

public class SaTenantDetails
{
    public SaTenantProfile tenant { get; set; } = new();
    public List<SaShopRow> shops { get; set; } = new();
    public List<SaBookRow> books { get; set; } = new();
    public List<SaLogRow> printLogs { get; set; } = new();
    public List<SaKeyRow> apiKeys { get; set; } = new();
}

public class SaShopRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UsersCount { get; set; }
    public int Prints { get; set; }
}

public class SaBookRow
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int Prints { get; set; }
}

public class SaLogRow
{
    public int Id { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public int Copies { get; set; }
    public DateTime PrintedAt { get; set; }
}

public class SaKeyRow
{
    public int Id { get; set; }
    public string prefix { get; set; } = string.Empty;
    public bool isActive { get; set; }
    public DateTime createdAt { get; set; }
    public int? shopId { get; set; }
    public string shopName { get; set; } = string.Empty;
}

public class PlatformSummary
{
    public PlatformTotals totals { get; set; } = new();
    public List<TrendDay> printTrends30d { get; set; } = new();
    public List<PlatformTenantRow> perTenant { get; set; } = new();
}

public class PlatformTotals
{
    public int tenants { get; set; }
    public int activeTenants { get; set; }
    public int shops { get; set; }
    public int books { get; set; }
    public int boards { get; set; }
    public int prints { get; set; }
}

public class TrendDay
{
    public DateTime date { get; set; }
    public int copies { get; set; }
}

public class PlatformTenantRow
{
    public int tenantId { get; set; }
    public string tenantName { get; set; } = string.Empty;
    public int shops { get; set; }
    public int prints { get; set; }
    public bool isActive { get; set; }
}

/// <summary>
/// SystemAdmin domain operations. Used directly by the Blazor Server pages
/// (circuits have no HttpContext, so HttpClient calls would be unauthenticated)
/// and by SystemAdminController for the JSON API + tests.
/// </summary>
public class SystemAdminService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApiKeyService _apiKeys;
    private readonly ILogger<SystemAdminService> _logger;

    public SystemAdminService(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IApiKeyService apiKeys,
        ILogger<SystemAdminService> logger)
    {
        _db = db;
        _userManager = userManager;
        _apiKeys = apiKeys;
        _logger = logger;
    }

    // ── Teachers (tenants) ──

    public async Task<List<TeacherRow>> ListTeachersAsync()
    {
        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var rows = new List<TeacherRow>();
        foreach (var t in tenants)
        {
            var shops = await _db.Shops.IgnoreQueryFilters().CountAsync(s => s.TenantId == t.Id);
            var books = await _db.Books.IgnoreQueryFilters().CountAsync(b => b.TenantId == t.Id);
            var boards = await _db.EducationalBoards.IgnoreQueryFilters().CountAsync(b => b.TenantId == t.Id);
            var prints = await _db.PrintLogs.IgnoreQueryFilters().Where(l => l.TenantId == t.Id).SumAsync(l => l.Copies);

            rows.Add(new TeacherRow
            {
                id = t.Id,
                name = t.Name,
                ownerName = t.OwnerName,
                contactEmail = t.ContactEmail,
                phone = t.Phone,
                isActive = t.IsActive,
                createdAt = t.CreatedAt,
                plan = t.Plan,
                maxShops = t.MaxShops,
                maxBooks = t.MaxBooks,
                stats = new TeacherRowStats { shops = shops, books = books, boards = boards, prints = prints }
            });
        }
        return rows;
    }

    /// <summary>Returns the created tenant id, or a user-facing error string.</summary>
    public async Task<(int? tenantId, string? error)> CreateTeacherAsync(CreateTeacherData request)
    {
        if (string.IsNullOrWhiteSpace(request?.Name) || string.IsNullOrWhiteSpace(request?.ContactEmail))
            return (null, "Name and contact email are required.");

        if (string.IsNullOrEmpty(request.Password))
            return (null, "Password is required.");

        if (await _userManager.FindByEmailAsync(request.ContactEmail) != null)
            return (null, "A user with this email already exists.");

        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            OwnerName = request.OwnerName,
            ContactEmail = request.ContactEmail.Trim(),
            Phone = request.Phone,
            IsActive = true,
            MaxShops = request.MaxShops,
            MaxBooks = request.MaxBooks,
            Plan = request.Plan
        };

        try
        {
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = request.ContactEmail.Trim(),
                Email = request.ContactEmail.Trim(),
                FullName = request.OwnerName ?? request.Name,
                EmailConfirmed = true,
                TenantId = tenant.Id
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                _db.Tenants.Remove(tenant);
                await _db.SaveChangesAsync();
                return (null, string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            await _userManager.AddToRoleAsync(user, "Teacher");

            _logger.LogInformation("SystemAdmin created tenant {TenantId} ({Name}) with account {Email}", tenant.Id, tenant.Name, user.Email);
            return (tenant.Id, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tenant {Name}", request.Name);
            return (null, "Failed to create teacher.");
        }
    }

    public async Task<(bool ok, string? error)> UpdateTeacherAsync(int id, UpdateTeacherData request)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return (false, "Teacher not found.");

        tenant.Name = string.IsNullOrWhiteSpace(request?.Name) ? tenant.Name : request.Name.Trim();
        tenant.OwnerName = request?.OwnerName;
        tenant.Phone = request?.Phone;
        tenant.MaxShops = request?.MaxShops;
        tenant.MaxBooks = request?.MaxBooks;
        tenant.Plan = request?.Plan;

        if (!string.IsNullOrWhiteSpace(request?.ContactEmail))
        {
            var owner = await _userManager.Users.FirstOrDefaultAsync(u => u.TenantId == id && u.Email != "sysadmin@drbahigPortal.com");
            var other = await _userManager.FindByEmailAsync(request.ContactEmail.Trim());
            if (other != null && (owner == null || other.Id != owner.Id))
                return (false, "A user with this email already exists.");

            tenant.ContactEmail = request.ContactEmail.Trim();
            if (owner != null)
            {
                owner.Email = request.ContactEmail.Trim();
                owner.UserName = request.ContactEmail.Trim();
                await _userManager.UpdateAsync(owner);
            }
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> SetTeacherActiveAsync(int id, bool active)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return (false, "Teacher not found.");

        tenant.IsActive = active;
        await _db.SaveChangesAsync();
        if (!active)
            _logger.LogWarning("SystemAdmin deactivated tenant {TenantId} ({Name})", tenant.Id, tenant.Name);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ResetTeacherPasswordAsync(int id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            return (false, "New password is required.");

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.TenantId == id && u.Email != "sysadmin@drbahigPortal.com");
        if (user == null)
            return (false, "Teacher account not found.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return (false, "Password does not meet requirements.");

        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteTeacherAsync(int id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return (false, "Teacher not found.");

        var hasShops = await _db.Shops.IgnoreQueryFilters().AnyAsync(s => s.TenantId == id);
        var hasBooks = await _db.Books.IgnoreQueryFilters().AnyAsync(b => b.TenantId == id);
        var hasBoards = await _db.EducationalBoards.IgnoreQueryFilters().AnyAsync(b => b.TenantId == id);
        var hasLogs = await _db.PrintLogs.IgnoreQueryFilters().AnyAsync(l => l.TenantId == id);
        if (hasShops || hasBooks || hasBoards || hasLogs)
            return (false, "Tenant has data (shops/books/boards/print logs). Deactivate instead.");

        var users = await _userManager.Users.Where(u => u.TenantId == id).ToListAsync();
        foreach (var u in users)
            await _userManager.DeleteAsync(u);

        var keys = await _db.TenantApiKeys.Where(k => k.TenantId == id).ToListAsync();
        _db.TenantApiKeys.RemoveRange(keys);
        _db.Tenants.Remove(tenant);
        await _db.SaveChangesAsync();

        return (true, null);
    }

    // ── Analytics ──

    public async Task<PlatformSummary> GetPlatformSummaryAsync()
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var activeTenants = tenants.Count(t => t.IsActive);
        var shops = await _db.Shops.IgnoreQueryFilters().CountAsync();
        var books = await _db.Books.IgnoreQueryFilters().CountAsync();
        var boards = await _db.EducationalBoards.IgnoreQueryFilters().CountAsync();
        var prints = await _db.PrintLogs.IgnoreQueryFilters().SumAsync(l => l.Copies);

        var now = DateTime.UtcNow;
        var trend = await _db.PrintLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.PrintedAt >= now.AddDays(-30))
            .GroupBy(l => l.PrintedAt.Date)
            .Select(g => new TrendDay { date = g.Key, copies = g.Sum(l => l.Copies) })
            .OrderBy(x => x.date)
            .ToListAsync();

        var perTenant = new List<PlatformTenantRow>();
        foreach (var t in tenants)
        {
            var tShops = await _db.Shops.IgnoreQueryFilters().CountAsync(s => s.TenantId == t.Id);
            var tPrints = await _db.PrintLogs.IgnoreQueryFilters().Where(l => l.TenantId == t.Id).SumAsync(l => l.Copies);
            perTenant.Add(new PlatformTenantRow
            {
                tenantId = t.Id,
                tenantName = t.Name,
                shops = tShops,
                prints = tPrints,
                isActive = t.IsActive
            });
        }

        return new PlatformSummary
        {
            totals = new PlatformTotals
            {
                tenants = tenants.Count,
                activeTenants = activeTenants,
                shops = shops,
                books = books,
                boards = boards,
                prints = prints
            },
            printTrends30d = trend,
            perTenant = perTenant
        };
    }

    // ── Tenant drill-down ──

    public async Task<SaTenantDetails?> GetTenantDetailsAsync(int id)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return null;

        var shops = await _db.Shops.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == id)
            .Select(s => new SaShopRow
            {
                Id = s.Id,
                Name = s.Name,
                UsersCount = _db.Users.Count(u => u.ShopId == s.Id),
                Prints = _db.PrintLogs.IgnoreQueryFilters().Where(l => l.ShopId == s.Id).Sum(l => l.Copies)
            })
            .ToListAsync();

        var books = await _db.Books.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.TenantId == id)
            .Select(b => new SaBookRow
            {
                Id = b.Id,
                Title = b.Title,
                PageCount = b.PageCount,
                Prints = _db.PrintLogs.IgnoreQueryFilters().Where(l => l.BookId == b.Id).Sum(l => l.Copies)
            })
            .ToListAsync();

        var printLogs = await _db.PrintLogs.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.TenantId == id)
            .OrderByDescending(l => l.PrintedAt)
            .Take(100)
            .Select(l => new SaLogRow
            {
                Id = l.Id,
                ShopName = l.ShopName,
                BookTitle = l.BookTitle,
                Copies = l.Copies,
                PrintedAt = l.PrintedAt
            })
            .ToListAsync();

        var apiKeys = await _db.TenantApiKeys.AsNoTracking()
            .Where(k => k.TenantId == id)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new SaKeyRow
            {
                Id = k.Id,
                prefix = k.KeyPrefix,
                isActive = k.IsActive,
                createdAt = k.CreatedAt,
                shopId = k.ShopId,
                shopName = k.Shop != null ? k.Shop.Name : ""
            })
            .ToListAsync();

        return new SaTenantDetails
        {
            tenant = new SaTenantProfile
            {
                id = tenant.Id,
                name = tenant.Name,
                ownerName = tenant.OwnerName,
                contactEmail = tenant.ContactEmail,
                phone = tenant.Phone,
                isActive = tenant.IsActive,
                createdAt = tenant.CreatedAt,
                plan = tenant.Plan
            },
            shops = shops,
            books = books,
            printLogs = printLogs,
            apiKeys = apiKeys
        };
    }

    // ── API keys ──

    public async Task<bool> TenantExistsAsync(int id) =>
        await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == id);

    public async Task<List<SaKeyRow>> ListKeysAsync(int id)
    {
        return await _db.TenantApiKeys.AsNoTracking()
            .Where(k => k.TenantId == id)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new SaKeyRow
            {
                Id = k.Id,
                prefix = k.KeyPrefix,
                isActive = k.IsActive,
                createdAt = k.CreatedAt,
                shopId = k.ShopId,
                shopName = k.Shop != null ? k.Shop.Name : ""
            })
            .ToListAsync();
    }

    public async Task<(string apiKey, string prefix)?> GenerateKeyAsync(int id)
    {
        if (!await TenantExistsAsync(id))
            return null;

        var plainKey = _apiKeys.GenerateKey(id);
        var prefix = plainKey[..12]; // "bpk_" + 8 chars
        _logger.LogInformation("New API key generated for tenant {TenantId} (prefix {Prefix})", id, prefix);
        return (plainKey, prefix);
    }

    public async Task<(bool ok, string? error)> RevokeKeyAsync(int tenantId, int keyId)
    {
        var key = await _db.TenantApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.TenantId == tenantId);
        if (key == null)
            return (false, "API key not found.");

        await _apiKeys.RevokeKeyAsync(keyId);
        _logger.LogWarning("API key {KeyId} revoked for tenant {TenantId}", keyId, tenantId);
        return (true, null);
    }
}