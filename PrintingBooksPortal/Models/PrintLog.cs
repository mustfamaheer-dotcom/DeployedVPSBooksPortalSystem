using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class PrintLog
{
    public int Id { get; set; }

    public int ShopId { get; set; }
    public int BookId { get; set; }

    [MaxLength(50)]
    public string ShopName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string BookTitle { get; set; } = string.Empty;

    public int Copies { get; set; } = 1;

    /// <summary>Selected pages, canonical form: "All" or e.g. "1-5, 8, 11-13".</summary>
    [MaxLength(200)]
    public string? Pages { get; set; }

    [MaxLength(100)]
    public string? PrintedByUserId { get; set; }

    [MaxLength(100)]
    public string? PrintedByUserName { get; set; }

    [MaxLength(50)]
    public string? IPAddress { get; set; }

    public DateTime PrintedAt { get; set; } = DateTime.UtcNow;

    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Shop Shop { get; set; } = null!;
    public Book Book { get; set; } = null!;
}
