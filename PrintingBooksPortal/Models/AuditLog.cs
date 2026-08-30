using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class AuditLog
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string ActorUserId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ActorRole { get; set; } = string.Empty;

    public int? TenantId { get; set; } // null for cross-tenant actions
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    [Required, MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? EntityId { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
