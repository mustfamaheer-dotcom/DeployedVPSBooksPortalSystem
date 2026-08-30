using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/student")]
[Authorize(Roles = "Student")]
public class StudentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPdfSecurityService _pdfSecurity;
    private readonly FileStorageService _storage;
    private readonly IWatermarkService _watermark;
    private readonly ISettingsService _settings;

    public StudentController(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IPdfSecurityService pdfSecurity,
        FileStorageService storage,
        IWatermarkService watermark,
        ISettingsService settings)
    {
        _db = db;
        _userManager = userManager;
        _pdfSecurity = pdfSecurity;
        _storage = storage;
        _watermark = watermark;
        _settings = settings;
    }

    [HttpGet("view/{bookId:int}")]
    public async Task<IActionResult> ViewSecurePdf(int bookId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        // Check if student has active access to this book
        var access = await _db.StudentBookAccesses
            .Include(a => a.Book)
            .Include(a => a.Tenant)
            .FirstOrDefaultAsync(a => a.StudentUserId == user.Id && a.BookId == bookId && a.IsActive);

        if (access == null)
            return Forbid("You do not have access to this book.");

        var book = access.Book;

        var path = _storage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(path))
            return NotFound("PDF file not found on disk");

        var pdfBytes = await System.IO.File.ReadAllBytesAsync(path);

        // Check if watermark is enabled for this specific teacher (tenant)
        var wmEnabledSetting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.WatermarkEnabled && s.TenantId == access.TenantId);
            
        bool isWatermarkEnabled = wmEnabledSetting?.ValueBool ?? true;

        if (!isWatermarkEnabled)
        {
            return File(pdfBytes, "application/pdf");
        }

        string wmTemplate = await _settings.GetWatermarkTextAsync();
        
        // Q1 answer: Show student name on the watermark
        string wmText = wmTemplate
            .Replace("{tenantName}", access.Tenant.Name)
            .Replace("{shopName}", "STUDENT COPY")
            .Replace("{userName}", user.FullName ?? user.UserName)
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        var watermarkedBytes = _watermark.ApplyWatermarkWithTenant(pdfBytes, access.Tenant.Name, "STUDENT COPY", user.FullName ?? user.UserName ?? "", DateTime.Now, true, wmText);

        return File(watermarkedBytes, "application/pdf");
    }
}
