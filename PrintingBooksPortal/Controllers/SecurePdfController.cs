using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api/pdf")]
public class SecurePdfController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FileStorageService _fileStorage;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PrintLoggingService _printLogging;
    private readonly IWatermarkService _watermarkService;
    private readonly ISettingsService _settingsService;
    private readonly PrintTokenService _printTokenService;
    private readonly IPdfSecurityService _pdfSecurity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecurePdfController> _logger;
    private readonly IApiKeyService _apiKeys;
    private readonly ITenantContext _tenantContext;

    public SecurePdfController(
        AppDbContext db,
        FileStorageService fileStorage,
        UserManager<ApplicationUser> userManager,
        PrintLoggingService printLogging,
        IWatermarkService watermarkService,
        ISettingsService settingsService,
        PrintTokenService printTokenService,
        IPdfSecurityService pdfSecurity,
        IConfiguration configuration,
        ILogger<SecurePdfController> logger,
        IApiKeyService apiKeys,
        ITenantContext tenantContext)
    {
        _db = db;
        _fileStorage = fileStorage;
        _userManager = userManager;
        _printLogging = printLogging;
        _watermarkService = watermarkService;
        _settingsService = settingsService;
        _printTokenService = printTokenService;
        _pdfSecurity = pdfSecurity;
        _configuration = configuration;
        _logger = logger;
        _apiKeys = apiKeys;
        _tenantContext = tenantContext;
    }

    // ── auth helpers ──

    // When true, a valid agent API key serves print jobs of ALL tenants.
    // Used by single-agent deployments (one print center PC serving every shop).
    // Default (false) keeps strict per-tenant agent scoping.
    private bool ServeAllTenants
        => _configuration.GetValue<bool?>("AgentSettings:ServeAllTenants") ?? false;

    private async Task<(Book? book, ApplicationUser? user)> ValidateAccess(int bookId)
    {
        var book = await _db.Books.Include(b => b.Board).FirstOrDefaultAsync(b => b.Id == bookId && b.IsActive);
        if (book == null)
            return (null, null);

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return (null, null);

        var isPrivileged = await _userManager.IsInRoleAsync(user, "Teacher")
                        || await _userManager.IsInRoleAsync(user, "SystemAdmin");
        var isOwnTenant = user.TenantId.HasValue && book.TenantId == user.TenantId.Value;
        if (isPrivileged && isOwnTenant)
            return (book, user);

        var hasAccess = await _db.ShopBookAssignments
            .AnyAsync(a => a.ShopId == user.ShopId && a.BookId == bookId && a.IsActive);

        return hasAccess ? (book, user) : (null, null);
    }

    private async Task<bool> IsJobOwnerAsync(string jobId, ClaimsPrincipal user)
    {
        if (!PendingPrintJobs.Jobs.TryGetValue(jobId, out var info))
        {
            // fall back to the claimed-but-active jobs (waiting for release / download)
            if (!ActiveJobStore.Jobs.TryGetValue(jobId, out info))
                return false;
        }

        var appUser = await _userManager.GetUserAsync(user);
        if (appUser == null) return false;

        var isPrivileged = await _userManager.IsInRoleAsync(appUser, "Teacher")
                        || await _userManager.IsInRoleAsync(appUser, "SystemAdmin");

        // Tenant check: privileged users can access own-tenant jobs only (§5.2)
        if (isPrivileged)
        {
            return _tenantContext.TenantId > 0 && info.TenantId == _tenantContext.TenantId;
        }

        // Shop users: job must belong to their shop AND their tenant
        return info.ShopId == appUser.ShopId && info.TenantId == appUser.TenantId;
    }

    // ── viewing / printing ──

    [HttpGet("view-secure/{bookId}")]
    [Authorize(Roles = "Shop,Teacher,SystemAdmin")]
    public async Task<IActionResult> ViewSecurePdf(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound(new { error = "Access Denied: You are not authorized to view this book." });

        var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
        var shopName = shop?.Name ?? "Unknown Shop";

        _logger.LogInformation("User {UserId} viewing secure PDF for book {BookId}", user.Id, bookId);

        var viewFilePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(viewFilePath))
            return NotFound(new { error = "The book file is missing on the server. Please contact the administrator to re-upload it." });

        try
        {
            var tenant = await GetTenantNameAsync();
            var originalBytes = await System.IO.File.ReadAllBytesAsync(viewFilePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermarkWithTenant(originalBytes, tenant, shopName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            return Ok(new { pdfData = Convert.ToBase64String(watermarked), watermarkEnabled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heavy watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process PDF for viewing." });
        }
    }

    [HttpPost("process-print")]
    [Authorize(Roles = "Shop")]
    public async Task<IActionResult> ProcessPrint([FromBody] ProcessPrintRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user?.ShopId == null)
            return Unauthorized(new { success = false, error = "Access Denied: You are not authorized to print." });

        var tenantId = user.TenantId ?? _tenantContext.TenantId;

        var hasAccess = await _db.ShopBookAssignments
            .AnyAsync(a => a.ShopId == user.ShopId && a.BookId == request.BookId && a.IsActive);

        if (!hasAccess)
            return Forbid();

        var book = await _db.Books.FindAsync(request.BookId);
        if (book == null)
            return NotFound(new { success = false, error = "Book not found." });

        // Ship-level fail-closed: the book must belong to the same tenant
        if (book.TenantId != tenantId)
            return NotFound(new { success = false, error = "Book not found." });

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { success = false, error = "PDF file not found on server." });

        var shop = await _db.Shops.FindAsync(user.ShopId.Value);
        var shopName = shop?.Name ?? "Unknown Shop";
        var copies = Math.Max(1, request.Copies);

        var jobId = Guid.NewGuid().ToString("N");
        var userPass = $"PRINT-{jobId}";
        // Security: read OwnerPassword from config or env var; fail if unset in production
        var ownerPass = _configuration.GetValue<string>("OwnerPassword__KeyVaultOrEnvVar");
        if (string.IsNullOrEmpty(ownerPass))
            ownerPass = Environment.GetEnvironmentVariable("OWNER_PASSWORD");
        if (string.IsNullOrEmpty(ownerPass))
            throw new InvalidOperationException("OwnerPassword is not configured. Set OwnerPassword__KeyVaultOrEnvVar in config or OWNER_PASSWORD environment variable.");

        _logger.LogInformation("ProcessPrint: Job={JobId}, Book={BookId}, Shop={ShopId}, Tenant={TenantId}, Copies={Copies}",
            jobId, request.BookId, user.ShopId, tenantId, copies);

        try
        {
            var tenantName = await GetTenantNameAsync();
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermarkWithTenant(originalBytes, tenantName, shopName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            var securedBytes = _pdfSecurity.EncryptPdfWithPassword(watermarked, userPass, ownerPass);

            var secureDir = SecurePrintsPath.GetSecureDir(tenantId);
            Directory.CreateDirectory(secureDir);
            var securePath = Path.Combine(secureDir, $"{jobId}.pdf");
            await System.IO.File.WriteAllBytesAsync(securePath, securedBytes);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _printLogging.LogPrintAsync(tenantId, user.ShopId.Value, request.BookId, copies, user.Id, user.UserName);

            _logger.LogInformation("Print logged: Job={JobId}, Shop={ShopId}, Book={BookId}, Copies={Copies}, IP={IP}",
                jobId, user.ShopId, request.BookId, copies, ipAddress);

            // Track ownership so only the creating shop (or TenantAdmin) can download/print the secured file
            var added = PendingPrintJobs.Jobs.TryAdd(jobId, new PendingJobInfo
            {
                TenantId = tenantId,
                ShopId = user.ShopId.Value,
                Copies = copies,
                CreatedAt = DateTime.UtcNow,
                PrinterName = request.PrinterName,
                PaperSize = request.PaperSize ?? "A4",
                Duplex = request.Duplex ?? "off",
                ScalingMode = request.ScalingMode ?? "actual",
                CustomScale = request.CustomScale ?? 100,
                MarginUnit = request.MarginUnit ?? "mm",
                MarginTop = request.MarginTop ?? 25.4,
                MarginBottom = request.MarginBottom ?? 25.4,
                MarginLeft = request.MarginLeft ?? 25.4,
                MarginRight = request.MarginRight ?? 25.4
            });

            _logger.LogInformation("Pending queue: Job={JobId} added={Added}, queueSize={Size}", jobId, added, PendingPrintJobs.Jobs.Count);

            return Ok(new
            {
                success = true,
                jobId,
                added,
                queueCount = PendingPrintJobs.Jobs.Count,
                watermarkEnabled,
                printerName = request.PrinterName,
                message = $"Print job {jobId} created for {copies} copy(ies).",
                printEndpoint = $"/api/pdf/print-file/{jobId}",
                downloadEndpoint = $"/api/pdf/download-secured/{jobId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessPrint failed for book {BookId}", request.BookId);
            // Security: never expose internal exception details to the client
            return StatusCode(500, new { success = false, error = "Failed to process print job." });
        }
    }

    [HttpGet("print-file/{jobId}")]
    [Authorize(Roles = "Shop,Teacher,SystemAdmin")]
    public async Task<IActionResult> GetPrintFile(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        // Verify the job belongs to the current user's shop/tenant (or Teacher/SystemAdmin)
        if (!await IsJobOwnerAsync(jobId, User))
            return Forbid();

        var job = PendingPrintJobs.Jobs.TryGetValue(jobId, out var info) ? info
                : (ActiveJobStore.Jobs.TryGetValue(jobId, out var active) ? active : null);
        var tenantId = job?.TenantId ?? _tenantContext.TenantId;

        var securePath = Path.Combine(SecurePrintsPath.GetSecureDir(tenantId), $"{jobId}.pdf");
        if (!System.IO.File.Exists(securePath))
            return NotFound("Print job not found or expired.");

        var fileBytes = System.IO.File.ReadAllBytes(securePath);
        System.IO.File.Delete(securePath);

        return File(fileBytes, "application/pdf", $"print_{jobId}.pdf");
    }

    [HttpGet("download-secured/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadSecured(string jobId)
    {
        if (!Guid.TryParse(jobId, out _))
            return BadRequest(new { error = "Invalid job ID format." });

        // Agent path: API key resolves a tenant (§7.3)
        var agentKey = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var agentTenantId = !string.IsNullOrEmpty(agentKey) ? await _apiKeys.ResolveTenantAsync(agentKey) : 0;

        int tenantId;
        if (agentTenantId > 0)
        {
            var job = PendingPrintJobs.Jobs.TryGetValue(jobId, out var info) ? info
                    : (ActiveJobStore.Jobs.TryGetValue(jobId, out var active) ? active : null);
            if (job == null)
                return NotFound("Print job not found or expired.");
            if (job.TenantId != agentTenantId && !ServeAllTenants)
                return Forbid();   // cross-tenant download blocked
            tenantId = ServeAllTenants ? job.TenantId : agentTenantId;
        }
        else
        {
            // Browser path: authenticated owner
            if (!(User.Identity?.IsAuthenticated == true))
                return Unauthorized();

            if (!await IsJobOwnerAsync(jobId, User))
                return Forbid();

            var job = PendingPrintJobs.Jobs.TryGetValue(jobId, out var info) ? info
                    : (ActiveJobStore.Jobs.TryGetValue(jobId, out var active) ? active : null);
            tenantId = job?.TenantId ?? _tenantContext.TenantId;
        }

        var securePath = Path.Combine(SecurePrintsPath.GetSecureDir(tenantId), $"{jobId}.pdf");
        if (!System.IO.File.Exists(securePath))
            return NotFound("Print job not found or expired.");

        var fileBytes = System.IO.File.ReadAllBytes(securePath);
        return File(fileBytes, "application/pdf", $"secured_{jobId}.pdf");
    }

    [HttpGet("print/{bookId}")]
    public async Task<IActionResult> PrintPdf(int bookId, [FromQuery] string? token = null)
    {
        Book? book = null;
        ApplicationUser? user = null;
        string shopName = "Unknown Shop";
        string userId = "unknown";
        string userName = "Unknown User";

        if (!string.IsNullOrEmpty(token))
        {
            if (_printTokenService.ValidateToken(token, out int tokenBookId, out int tokenTenantId, out userId, out shopName, out userName))
            {
                book = await _db.Books.Include(b => b.Board).FirstOrDefaultAsync(b => b.Id == tokenBookId && b.IsActive);
                if (book == null)
                    return NotFound();
                if (book.TenantId != tokenTenantId)
                    return NotFound();   // cross-tenant token rejected
            }
            else
            {
                return Unauthorized("Invalid or expired print token.");
            }
        }
        else
        {
            (book, user) = await ValidateAccess(bookId);
            if (book == null || user == null)
                return NotFound();

            var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
            shopName = shop?.Name ?? "Unknown Shop";
            userId = user.Id;
            userName = user.UserName ?? "Unknown";
        }

        var filePath = _fileStorage.GetFilePath(book.FilePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound("PDF file not found on server.");

        _logger.LogInformation("Print request for book {BookId} (tenant {TenantId}) by {UserName} (Shop: {ShopName})", bookId, book.TenantId, userName, shopName);

        try
        {
            var tenantName = await GetTenantNameAsync();
            var originalBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermarkWithTenant(originalBytes, tenantName, shopName, userName, DateTime.UtcNow, watermarkEnabled, watermarkText);
            return File(new MemoryStream(watermarked), "application/pdf", enableRangeProcessing: false);
        }
        catch (Exception ex)
        {
            // Security: fail CLOSED — never expose the unwatermarked file
            _logger.LogError(ex, "Watermarking failed for book {BookId}", bookId);
            return StatusCode(500, new { error = "Failed to process secure document." });
        }
    }

    [HttpGet("print-token/{bookId}")]
    [Authorize(Roles = "Shop")]
    public async Task<IActionResult> GetPrintToken(int bookId)
    {
        var (book, user) = await ValidateAccess(bookId);
        if (book == null || user == null)
            return NotFound();

        var shop = user.ShopId != null ? await _db.Shops.FindAsync(user.ShopId.Value) : null;
        var shopName = shop?.Name ?? "Unknown Shop";

        var token = _printTokenService.GenerateToken(bookId, user.TenantId ?? 0, user.Id, shopName, user.UserName ?? "Unknown");
        return Ok(new { token, expiresInMinutes = 5 });
    }

    // ── agent endpoints (API key → tenant) ──

    [HttpGet("print-agent/pending")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPendingJobs()
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var isUser = User.Identity?.IsAuthenticated == true;
        var tenantId = 0;

        if (!string.IsNullOrEmpty(key))
            tenantId = await _apiKeys.ResolveTenantAsync(key);

        if (tenantId == 0 && !isUser)
            return Unauthorized(new { error = "Authentication required." });

        var cutoff = DateTime.UtcNow.Add(-PendingPrintJobs.Expiry);
        var expired = PendingPrintJobs.Jobs.Where(kv => kv.Value.CreatedAt < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in expired)
            PendingPrintJobs.Jobs.TryRemove(k, out _);

        var jobs = PendingPrintJobs.Jobs
            .Where(kv => ServeAllTenants || tenantId == 0 || kv.Value.TenantId == tenantId)
            .Select(kv => kv.Key)
            .ToList();

        _logger.LogInformation("GetPendingJobs returning {Count} jobs for tenant {TenantId}", jobs.Count, tenantId);
        return Ok(new { jobs, tenantId });
    }

    [HttpPost("print-agent/heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> AgentHeartbeat([FromBody] AgentHeartbeatRequest? request)
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key) || await _apiKeys.ResolveTenantAsync(key) == 0)
            return Unauthorized(new { error = "Valid API key required." });

        AgentStatusTracker.RecordHeartbeat(request?.Printers ?? new(), await _apiKeys.ResolveTenantAsync(key));
        return Ok(new { success = true });
    }

    [HttpGet("print-agent/status")]
    [AllowAnonymous]
    public async Task<IActionResult> AgentStatus()
    {
        return Ok(AgentStatusTracker.GetStatus());
    }

    [HttpGet("print-agent/debug")]
    [AllowAnonymous]
    public async Task<IActionResult> DebugPending()
    {
        var isUser = User.Identity?.IsAuthenticated == true;
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenantId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveTenantAsync(key) : 0;
        if (!isUser && tenantId == 0)
            return Unauthorized(new { error = "Authentication required." });

        var now = DateTime.UtcNow;
        var cutoff = now.Add(-PendingPrintJobs.Expiry);
        return Ok(new
        {
            jobCount = PendingPrintJobs.Jobs.Count,
            expiryMinutes = PendingPrintJobs.Expiry.TotalMinutes,
            now,
            jobs = PendingPrintJobs.Jobs.Select(kv => new
            {
                jobId = kv.Key,
                tenantId = kv.Value.TenantId,
                shopId = kv.Value.ShopId,
                copies = kv.Value.Copies,
                createdAt = kv.Value.CreatedAt,
                isExpired = kv.Value.CreatedAt < cutoff
            }).ToList()
        });
    }

    [HttpPost("print-agent/claim/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimJob(string jobId)
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenantId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveTenantAsync(key) : 0;
        if (tenantId == 0)
            return Unauthorized(new { error = "Valid API key required." });

        if (PendingPrintJobs.Jobs.TryRemove(jobId, out var info))
        {
            if (info.TenantId != tenantId && !ServeAllTenants)
            {
                PendingPrintJobs.Jobs.TryAdd(jobId, info); // restore — cross-tenant claim blocked
                return StatusCode(403, new { success = false, error = "Job belongs to another tenant." });
            }

            ActiveJobStore.Jobs[jobId] = info; // remember for release/download
            return Ok(new
            {
                success = true,
                jobId,
                tenantId = info.TenantId,
                copies = info.Copies,
                printerName = info.PrinterName,
                paperSize = info.PaperSize ?? "A4",
                duplex = info.Duplex ?? "off",
                scalingMode = info.ScalingMode ?? "actual",
                customScale = info.CustomScale ?? 100,
                marginUnit = info.MarginUnit ?? "mm",
                marginTop = info.MarginTop ?? 0,
                marginBottom = info.MarginBottom ?? 0,
                marginLeft = info.MarginLeft ?? 0,
                marginRight = info.MarginRight ?? 0
            });
        }
        return NotFound(new { success = false, error = "Job not found or already claimed." });
    }

    [HttpPost("print-agent/release/{jobId}")]
    [AllowAnonymous]
    public async Task<IActionResult> ReleaseJob(string jobId)
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenantId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveTenantAsync(key) : 0;
        if (tenantId == 0)
            return Unauthorized(new { error = "Valid API key required." });

        // Only re-add if not already in the queue — reuse the claimed info (incl. TenantId + settings)
        if (!PendingPrintJobs.Jobs.ContainsKey(jobId) && ActiveJobStore.Jobs.TryRemove(jobId, out var info))
        {
            if (info.TenantId != tenantId && !ServeAllTenants)
                return StatusCode(403, new { success = false, error = "Job belongs to another tenant." });

            info.CreatedAt = DateTime.UtcNow; // restart expiry clock
            PendingPrintJobs.Jobs.TryAdd(jobId, info);
            return Ok(new { success = true, message = "Job returned to pending queue." });
        }
        return Ok(new { success = true, message = "Job already in queue." });
    }

    private async Task<string> GetTenantNameAsync()
    {
        var tid = _tenantContext.TenantId;
        if (tid <= 0) return "";
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid);
        return tenant?.Name ?? "";
    }
}

public static class SecurePrintsPath
{
    public static string GetSecureDir(int tenantId)
        => Path.Combine(Directory.GetCurrentDirectory(), "SecurePrints", tenantId.ToString());
}

public class ProcessPrintRequest
{
    public int BookId { get; set; }

    [Range(1, 50, ErrorMessage = "Copies must be between 1 and 50.")]
    public int Copies { get; set; } = 1;

    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}

public class PendingJobInfo
{
    public int TenantId { get; set; }          // NEW — required
    public int ShopId { get; set; }
    public int Copies { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PrinterName { get; set; }
    public string? PaperSize { get; set; }
    public string? Duplex { get; set; }
    public string? ScalingMode { get; set; }
    public int? CustomScale { get; set; }
    public string? MarginUnit { get; set; }
    public double? MarginTop { get; set; }
    public double? MarginBottom { get; set; }
    public double? MarginLeft { get; set; }
    public double? MarginRight { get; set; }
}

/// <summary>Jobs claimed by an agent but not yet downloaded/deleted — used to restore on release (§7.3).</summary>
public static class ActiveJobStore
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, PendingJobInfo> Jobs = new();
}

public static class PendingPrintJobs
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, PendingJobInfo> Jobs = new();
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);
}

public static class AgentStatusTracker
{
    private static DateTime _lastSeen = DateTime.MinValue;
    private static List<AgentPrinterInfo> _printers = new();
    private static int _tenantId;
    private static readonly object _lock = new();

    public static void RecordHeartbeat(List<AgentPrinterInfo> printers, int tenantId)
    {
        lock (_lock)
        {
            _lastSeen = DateTime.UtcNow;
            _printers = printers ?? new();
            _tenantId = tenantId;
        }
    }

    public static bool IsConnected => (DateTime.UtcNow - _lastSeen).TotalSeconds < 15;

    public static object GetStatus()
    {
        lock (_lock)
        {
            return new
            {
                connected = IsConnected,
                lastSeen = _lastSeen == DateTime.MinValue ? (string?)null : _lastSeen.ToString("O"),
                tenantId = _tenantId,
                printers = _printers
            };
        }
    }
}

public class AgentPrinterInfo
{
    public string Name { get; set; } = "";
    public string? ConnectionType { get; set; }
    public bool IsDefault { get; set; }
}

public class AgentHeartbeatRequest
{
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}