using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public enum NotificationType
{
    EnrollmentApproved,
    EnrollmentRejected,
    BookGranted,
    BookRevoked,
    Announcement
}

public class StudentNotification
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string StudentUserId { get; set; } = string.Empty;
    public ApplicationUser Student { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? RelatedUrl { get; set; }
}
