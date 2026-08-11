using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class Shop
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    // Encrypted (DataProtection) copy of the shop's login password, so the
    // administrator can view it later. Passwords set before this field existed
    // are not recoverable.
    [MaxLength(2000)]
    public string? StoredPassword { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public ICollection<ShopBookAssignment> Assignments { get; set; } = new List<ShopBookAssignment>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
}
