using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Teacher,SystemAdmin")]
[IgnoreAntiforgeryToken]   // JSON API called from Blazor circuits — no form antiforgery token is attached
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStorageService _fileStorage;
    private readonly ITenantContext _tenantContext;

    public AdminController(AppDbContext db, FileStorageService fileStorage, ITenantContext tenantContext)
    {
        _db = db;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
    }

    [HttpPost("books/upload")]
    public async Task<IActionResult> UploadBook([FromForm] BookUploadRequest request, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            // Metadata-only update is allowed for existing books; new books require a PDF.
            if (request.BookId <= 0)
                return BadRequest(new { success = false, error = "No PDF file received." });
        }
        else
        {
            if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, error = "Only PDF files are allowed." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { success = false, error = "Book title is required." });

        Book? book;
        if (request.BookId > 0)
        {
            book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId);
            if (book == null)
                return NotFound(new { success = false, error = "Book not found." });
        }
        else
        {
            if (request.BoardId <= 0)
                return BadRequest(new { success = false, error = "Please select a board." });

            // SystemAdmin has no tenant (TenantId = 0) and Books carries an FK to
            // Tenants, so newly created books fall back to the first active tenant.
            var tenantId = _tenantContext.TenantId > 0
                ? _tenantContext.TenantId
                : await _db.Tenants.Where(t => t.IsActive).OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync() ?? 0;
            if (tenantId <= 0)
                return BadRequest(new { success = false, error = "No active tenant is configured. Contact the administrator." });

            book = new Book { TenantId = tenantId };
            _db.Books.Add(book);
        }

        book.Title = request.Title.Trim();
        if (request.BoardId > 0)
            book.BoardId = request.BoardId;
        if (request.PageCount > 0)
            book.PageCount = request.PageCount;
        book.IsActive = request.IsActive;

        if (file != null && file.Length > 0)
        {
            string newFile;
            try
            {
                newFile = await _fileStorage.SaveFileAsync(file);
            }
            catch
            {
                return BadRequest(new { success = false, error = "Could not store the PDF file on the server." });
            }

            var oldFile = book.FilePath;
            book.FilePath = newFile;
            book.OriginalFileName = file.FileName;
            book.FileSizeBytes = file.Length;

            // Page count is detected automatically from the PDF (iText7 handles
            // xref-stream PDFs). Falls back to the submitted value only when the
            // PDF cannot be parsed.
            var detectedPages = PdfPageCounter.Count(_fileStorage.GetFilePath(newFile));
            if (detectedPages > 0)
                book.PageCount = detectedPages;

            await _db.SaveChangesAsync();
            // Remove the replaced version only after the new one is committed.
            if (!string.IsNullOrEmpty(oldFile))
                _fileStorage.DeleteFile(oldFile);
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        var message = request.BookId > 0
            ? $"Book '{book.Title}' updated successfully."
            : $"Book '{book.Title}' uploaded successfully.";
        if (book.PageCount > 0)
            message += $" ({book.PageCount} pages)";
        return Ok(new { success = true, message, bookId = book.Id, pageCount = book.PageCount });
    }

    [HttpPost("reset-shop-stats/{shopId:int}")]
    public async Task<IActionResult> ResetShopStats(int shopId, [FromBody] ResetRequest request)
    {
        if (request?.Password != "0000")
            return BadRequest(new { success = false, error = "Wrong password. Stats were NOT reset." });

        var logs = await _db.PrintLogs.Where(l => l.ShopId == shopId).ToListAsync();
        _db.PrintLogs.RemoveRange(logs);
        await _db.SaveChangesAsync();

        var shop = await _db.Shops.FindAsync(shopId);
        return Ok(new { success = true, message = $"Statistics reset for '{shop?.Name ?? "shop"}' ({logs.Count} log entries removed)." });
    }

    [HttpGet("shop-receipt/{shopId:int}")]
    public async Task<IActionResult> GetShopReceipt(int shopId)
    {
        var shop = await _db.Shops.FindAsync(shopId);
        if (shop == null)
            return NotFound(new { error = "Shop not found." });

        var logs = await _db.PrintLogs
            .Where(l => l.ShopId == shopId)
            .Include(l => l.Book)
            .OrderByDescending(l => l.PrintedAt)
            .ToListAsync();
        var totalPrints = logs.Sum(l => l.Copies);

        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);

        var fontTitle = new XFont("Arial", 18, XFontStyle.Bold);
        var fontHeader = new XFont("Arial", 13, XFontStyle.Bold);
        var fontBody = new XFont("Arial", 11, XFontStyle.Regular);
        var fontSmall = new XFont("Arial", 9, XFontStyle.Regular);
        var gray = XBrushes.Gray;
        var black = XBrushes.Black;
        var accent = new XSolidBrush(XColor.FromArgb(16, 185, 129));

        int y = 40;
        gfx.DrawString("DR Bahig Books Portal", fontTitle, accent, new XPoint(40, y));
        y += 30;
        gfx.DrawString("Print Receipt", fontHeader, black, new XPoint(40, y));
        y += 28;

        gfx.DrawString($"Shop: {shop.Name}", fontBody, black, new XPoint(40, y));
        y += 20;
        gfx.DrawString($"Date: {DateTime.UtcNow.ToEgyptLocal():dd/MM/yyyy HH:mm}", fontBody, gray, new XPoint(40, y));
        y += 20;
        gfx.DrawString("Phone: " + (shop.Phone ?? "\u2014"), fontBody, gray, new XPoint(40, y));
        y += 20;
        gfx.DrawString("Address: " + (shop.Address ?? "\u2014"), fontBody, gray, new XPoint(40, y));
        y += 30;

        gfx.DrawLine(new XPen(accent.Color, 2), 40, y, 550, y);
        y += 20;

        gfx.DrawString($"Total prints: {totalPrints}", fontHeader, black, new XPoint(40, y));
        y += 28;

        if (logs.Count > 0)
        {
            var rowFont = new XFont("Arial", 8, XFontStyle.Regular);
            var headerFont = new XFont("Arial", 8, XFontStyle.Bold);
            gfx.DrawString("Date/Time", headerFont, gray, new XPoint(40, y));
            gfx.DrawString("Book", headerFont, gray, new XPoint(135, y));
            gfx.DrawString("Pages", headerFont, gray, new XPoint(300, y));
            gfx.DrawString("Copies", headerFont, gray, new XPoint(370, y));
            gfx.DrawString("Printed By", headerFont, gray, new XPoint(415, y));
            gfx.DrawString("IP", headerFont, gray, new XPoint(500, y));
            y += 14;

            foreach (var log in logs)
            {
                var title = log.BookTitle.Length > 35 ? log.BookTitle[..35] : log.BookTitle;
                var by = (log.PrintedByUserName ?? "\u2014").Length > 30 ? (log.PrintedByUserName ?? "\u2014")[..30] : (log.PrintedByUserName ?? "\u2014");
                var ip = (log.IPAddress ?? "\u2014").Length > 24 ? (log.IPAddress ?? "\u2014")[..24] : (log.IPAddress ?? "\u2014");

                gfx.DrawString(log.PrintedAt.ToEgyptLocal().ToString("g"), rowFont, black, new XPoint(40, y));
                gfx.DrawString(title, rowFont, black, new XPoint(135, y));
                gfx.DrawString(PdfPageSelection.DescribePages(log.Pages, log.Book?.PageCount), rowFont, black, new XPoint(300, y));
                gfx.DrawString(log.Copies.ToString(), rowFont, black, new XPoint(370, y));
                gfx.DrawString(by, rowFont, black, new XPoint(415, y));
                gfx.DrawString(ip, rowFont, black, new XPoint(500, y));
                y += 13;

                if (y > 770)
                {
                    page = doc.AddPage();
                    page.Size = PageSize.A4;
                    gfx = XGraphics.FromPdfPage(page);
                    y = 40;
                }
            }
        }
        else
        {
            gfx.DrawString("No prints recorded for this shop.", fontBody, gray, new XPoint(50, y));
        }

        y = Math.Max(y + 30, 780);
        gfx.DrawLine(new XPen(XColor.FromArgb(200, 200, 200)), 40, y, 550, y);
        y += 16;
        gfx.DrawString("Generated by DR Bahig Books Portal Print System", fontSmall, gray, new XPoint(40, y));

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        var fileName = $"receipt_{shop.Name.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.pdf";
        return File(ms.ToArray(), "application/pdf", fileName);
    }
}

public class ResetRequest
{
    public string Password { get; set; } = "";
}

public class BookUploadRequest
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public int BoardId { get; set; }
    public int PageCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class PdfPageCounter
{
    /// <summary>Counts the pages of a PDF on disk. Returns 0 when the file cannot be parsed.</summary>
    public static int Count(string filePath)
    {
        try
        {
            using var reader = new iText.Kernel.Pdf.PdfReader(filePath);
            using var pdf = new iText.Kernel.Pdf.PdfDocument(reader);
            return pdf.GetNumberOfPages();
        }
        catch
        {
            return 0;
        }
    }
}
