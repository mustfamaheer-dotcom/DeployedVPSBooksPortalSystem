using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class TenantApiKey
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// When set, this key belongs to ONE shop only — the agent using it is that
    /// shop's agent, and its printers are shown/validated for that shop alone.
    /// Null means a tenant-wide key (used when no per-shop key exists yet).
    /// </summary>
    public int? ShopId { get; set; }
    public Shop? Shop { get; set; }

    [Required, MaxLength(64)] public string KeyHash { get; set; } = string.Empty;
    [Required, MaxLength(8)] public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}