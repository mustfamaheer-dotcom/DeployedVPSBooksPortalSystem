using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class Tenant
{
    public int Id { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? OwnerName { get; set; }
    [MaxLength(200)] public string? ContactEmail { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Future billing fields (reserved)
    public int? MaxShops { get; set; }
    public int? MaxBooks { get; set; }
    [MaxLength(50)] public string? Plan { get; set; }

    public ICollection<Shop> Shops { get; set; } = new List<Shop>();
    public ICollection<Book> Books { get; set; } = new List<Book>();
    public ICollection<EducationalBoard> Boards { get; set; } = new List<EducationalBoard>();
    public ICollection<ShopBookAssignment> Assignments { get; set; } = new List<ShopBookAssignment>();
    public ICollection<PrintLog> PrintLogs { get; set; } = new List<PrintLog>();
    public ICollection<SystemSetting> Settings { get; set; } = new List<SystemSetting>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<TenantApiKey> ApiKeys { get; set; } = new List<TenantApiKey>();

    public ICollection<StudentEnrollment> StudentEnrollments { get; set; } = new List<StudentEnrollment>();
    public ICollection<StudentBookAccess> StudentBookAccesses { get; set; } = new List<StudentBookAccess>();
    public ICollection<RegistrationRequest> RegistrationRequests { get; set; } = new List<RegistrationRequest>();
}