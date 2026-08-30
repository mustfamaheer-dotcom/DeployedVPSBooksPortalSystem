using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public enum RegistrationType
{
    Teacher,
    Bookshop
}

public enum RegistrationStatus
{
    Pending,
    Approved,
    Rejected
}

public class RegistrationRequest
{
    public int Id { get; set; }

    public RegistrationType Type { get; set; }

    [Required, MaxLength(200)]
    public string ApplicantName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required, MaxLength(200)]
    public string OrganizationName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(1000)]
    public string? Message { get; set; }

    [MaxLength(2000)]
    public string? StoredPassword { get; set; }

    public RegistrationStatus Status { get; set; } = RegistrationStatus.Pending;

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    public int? CreatedTenantId { get; set; }
    public Tenant? CreatedTenant { get; set; }

    public int? CreatedShopId { get; set; }
    public Shop? CreatedShop { get; set; }
}
