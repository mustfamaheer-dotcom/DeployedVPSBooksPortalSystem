using System.ComponentModel.DataAnnotations;

namespace PrintingBooksPortal.Models;

public class SystemSetting
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public bool ValueBool { get; set; }

    [MaxLength(2000)]
    public string? ValueString { get; set; }

    public int? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}

public static class SystemSettingKeys
{
    public const string WatermarkEnabled = "WatermarkEnabled";
    public const string WatermarkText = "WatermarkText";
    public const string DeveloperToolsEnabled = "DeveloperToolsEnabled";
}
