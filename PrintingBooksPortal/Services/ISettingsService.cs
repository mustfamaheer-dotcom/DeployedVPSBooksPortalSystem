namespace PrintingBooksPortal.Services;

public interface ISettingsService
{
    Task<bool> IsWatermarkEnabledAsync();
    Task SetWatermarkEnabledAsync(bool enabled);
    Task<string> GetWatermarkTextAsync();
    Task SetWatermarkTextAsync(string text);
    Task<bool> IsDeveloperToolsEnabledAsync();
    Task SetDeveloperToolsEnabledAsync(bool enabled);
}
