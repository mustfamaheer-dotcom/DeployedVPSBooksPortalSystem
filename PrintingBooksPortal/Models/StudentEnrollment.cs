using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public enum EnrollmentStatus
{
    Pending,
    Approved,
    Rejected
}

public class StudentEnrollment
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string StudentUserId { get; set; } = string.Empty;
    public ApplicationUser Student { get; set; } = null!;

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Pending;

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    [MaxLength(1000)]
    public string? StudentNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
