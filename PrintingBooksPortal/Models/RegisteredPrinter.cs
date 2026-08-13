using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class RegisteredPrinter
{
    public int Id { get; set; }
    
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Null for tenant-wide agents (legacy keys), otherwise the shop the agent belongs to.</summary>
    public int? ShopId { get; set; }
    public Shop? Shop { get; set; }
    
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string Port { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string ConnectionType { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string Driver { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string Comment { get; set; } = string.Empty;
    
    public bool IsDefault { get; set; }
    
    public bool IsOnline { get; set; } = true;
    
    [MaxLength(20)]
    public string Status { get; set; } = "Unknown";
    
    [Required, MaxLength(64)]
    public string AgentKeyHash { get; set; } = string.Empty;
    
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}