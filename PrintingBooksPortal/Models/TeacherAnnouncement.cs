using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class TeacherAnnouncement
{
    public int Id { get; set; }

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}
