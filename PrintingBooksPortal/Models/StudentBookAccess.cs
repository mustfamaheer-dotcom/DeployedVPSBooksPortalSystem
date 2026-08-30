using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class StudentBookAccess
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string StudentUserId { get; set; } = string.Empty;
    public ApplicationUser Student { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required, MaxLength(100)]
    public string GrantedByUserId { get; set; } = string.Empty;
    public ApplicationUser GrantedByUser { get; set; } = null!;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}
