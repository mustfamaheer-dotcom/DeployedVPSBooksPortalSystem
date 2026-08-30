using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class StudentReadingProgress
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string StudentUserId { get; set; } = string.Empty;
    public ApplicationUser Student { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public int LastPageRead { get; set; }
    public int TotalPages { get; set; }
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
}
