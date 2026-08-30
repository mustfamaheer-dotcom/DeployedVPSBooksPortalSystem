using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class StudentAnnotationRecord
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string StudentUserId { get; set; } = string.Empty;
    public ApplicationUser Student { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public int PageNumber { get; set; }

    public string AnnotationData { get; set; } = string.Empty; // JSON blob

    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;
}
