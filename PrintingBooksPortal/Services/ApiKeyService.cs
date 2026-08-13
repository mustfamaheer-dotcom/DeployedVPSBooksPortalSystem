using System.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public interface IApiKeyService
{
    /// <summary>"bpk_" + Guid:N; stores SHA-256 hash; returns plaintext once. shopId=null → tenant-wide key.</summary>
    string GenerateKey(int tenantId, int? shopId = null);
    /// <summary>Tenant the key belongs to; 0 if invalid/inactive.</summary>
    Task<int> ResolveTenantAsync(string apiKey);
    /// <summary>Shop the key is bound to; 0 for tenant-wide keys.</summary>
    Task<int> ResolveShopAsync(string apiKey);
    Task<bool> RevokeKeyAsync(int keyId);
    /// <summary>Keys of a tenant, optionally filtered to one shop (or tenant-wide when shopId == 0).</summary>
    Task<List<TenantApiKey>> ListKeysAsync(int tenantId, int? shopId = null);
}

public class ApiKeyService : IApiKeyService
{
    private readonly AppDbContext _db;

    public ApiKeyService(AppDbContext db)
    {
        _db = db;
    }

    public string GenerateKey(int tenantId, int? shopId = null)
    {
        var plain = "bpk_" + Guid.NewGuid().ToString("N");
        _db.TenantApiKeys.Add(new TenantApiKey
        {
            TenantId = tenantId,
            ShopId = shopId,
            KeyHash = HashKey(plain),
            KeyPrefix = plain[4..12]      // first 8 chars after "bpk_"
        });
        _db.SaveChanges();
        return plain;
    }

    public async Task<int> ResolveTenantAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        var hash = HashKey(key);
        var entry = await _db.TenantApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
        return entry?.TenantId ?? 0;
    }

    public async Task<int> ResolveShopAsync(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        var hash = HashKey(key);
        var entry = await _db.TenantApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
        return entry?.ShopId ?? 0;
    }

    public async Task<bool> RevokeKeyAsync(int keyId)
    {
        var entry = await _db.TenantApiKeys.FindAsync(keyId);
        if (entry == null) return false;
        entry.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<TenantApiKey>> ListKeysAsync(int tenantId, int? shopId = null)
    {
        var query = _db.TenantApiKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId);

        if (shopId.HasValue)
            query = query.Where(k => k.ShopId == shopId.Value);
        else
            query = query.Where(k => k.ShopId == null);

        return await query
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync();
    }

    private static string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}