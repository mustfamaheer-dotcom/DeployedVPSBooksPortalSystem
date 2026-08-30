using Microsoft.AspNetCore.Identity;

namespace PrintingBooksPortal.Models;

public class ApplicationUser : IdentityUser
{
    public int? ShopId { get; set; }
    public int? TenantId { get; set; }
    public bool MustChangePassword { get; set; }
    public string? FullName { get; set; }
    public Shop? Shop { get; set; }
    public Tenant? Tenant { get; set; }

    public bool IsStudent { get; set; }
    public ICollection<StudentEnrollment> Enrollments { get; set; } = new List<StudentEnrollment>();
    public ICollection<StudentBookAccess> BookAccesses { get; set; } = new List<StudentBookAccess>();
}