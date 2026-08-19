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
    // Agent heartbeats every ~3s; mirror the tracker's thresholds for UI status.
    private const double OfflineAfterSeconds = 60;
    private const double StaleAfterSeconds = 30;

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
    private readonly IPrinterRegistrationService _printerRegistration;

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
        ITenantContext tenantContext,
        IPrinterRegistrationService printerRegistration)
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
        _printerRegistration = printerRegistration;
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

        // Size guard: refuse to attempt processing pathologically large files (fail closed,
        // no partial/wrong output). Books up to this limit are streamed page-by-page via
        // HTTP Range requests, so even very large scanned books remain viewable.
        const long maxViewBytes = 512L * 1024 * 1024;
        if (new System.IO.FileInfo(viewFilePath).Length > maxViewBytes)
            return StatusCode(413, new { error = "This book is too large to view online. Please contact the administrator." });

        try
        {
            var tenantName = await GetTenantNameAsync();
            var userName = user.UserName ?? "Unknown";
            var tenantId = user.TenantId ?? _tenantContext.TenantId;
            var dayKey = DateTime.UtcNow.ToString("yyyyMMdd");

            // Watermarked view copies are cached per book+shop+user+day (the watermark text
            // embeds shop, user and date). Without a cache, pdf.js issues many Range requests
            // and each one would re-watermark the whole file. Generating once per day per user
            // makes large books usable; stale entries are pruned lazily on the next hit.
            var cacheDir = ViewCachePath.GetViewDir(tenantId);
            Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, $"{bookId}_{user.ShopId?.ToString() ?? "n"}_{user.Id}_{dayKey}.pdf");

            if (!System.IO.File.Exists(cacheFile))
            {
                ViewCachePath.PruneStaleViews(cacheDir, dayKey);

                var originalBytes = await System.IO.File.ReadAllBytesAsync(viewFilePath);
                var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
                var watermarkText = await _settingsService.GetWatermarkTextAsync();
                var watermarked = _watermarkService.ApplyWatermarkWithTenant(originalBytes, tenantName, shopName, userName, DateTime.UtcNow, watermarkEnabled, watermarkText);

                // Atomic write (temp + rename) so concurrent viewers never see a partial file.
                var tmp = cacheFile + ".tmp";
                await System.IO.File.WriteAllBytesAsync(tmp, watermarked);
                System.IO.File.Move(tmp, cacheFile, overwrite: true);
            }

            _logger.LogInformation("User {UserId} served secure PDF for book {BookId} from {CacheFile}", user.Id, bookId, cacheFile);
            // enableRangeProcessing lets pdf.js fetch only the pages it needs (huge files OK).
            return File(new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read), "application/pdf", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            // Security: fail CLOSED — never expose the unwatermarked file
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

        // Accuracy gate: never queue a job for a printer the agent does not currently
        // detect — otherwise the job would silently fail or fall back to the wrong printer.
        // Skipped when the agent has not reported a printer list yet (cannot validate).
        // Shop users are validated against THEIR OWN shop's agent only (unless the
        // single-agent "ServeAllTenants" mode is enabled — then any agent's printers count).
        var shopScope = ServeAllTenants ? (int?)null : user.ShopId;
        var printerCheck = await _printerRegistration.HasPrinterAsync(request.PrinterName, tenantId, shopScope);
        if (printerCheck == false)
            return BadRequest(new
            {
                success = false,
                error = $"The selected printer \"{request.PrinterName}\" is not currently detected by the agent on this computer. Refresh the printer list and try again."
            });

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

            // Page selection: validate against the REAL PDF page count (never trust DB PageCount).
            // Fail closed: any invalid selection is rejected before anything is queued.
            var totalPages = PdfPageSelection.CountPages(originalBytes);
            if (!PdfPageSelection.TryParse(request.Pages, totalPages, out var selectedPages, out var pageError))
                return BadRequest(new { success = false, error = pageError });

            var pageSummary = PdfPageSelection.FormatPages(selectedPages);
            var printSource = selectedPages.Count > 0
                ? PdfPageSelection.ExtractPages(originalBytes, selectedPages)
                : originalBytes;

            var watermarkEnabled = await _settingsService.IsWatermarkEnabledAsync();
            var watermarkText = await _settingsService.GetWatermarkTextAsync();
            var watermarked = _watermarkService.ApplyWatermarkWithTenant(printSource, tenantName, shopName, user.UserName ?? "Unknown", DateTime.UtcNow, watermarkEnabled, watermarkText);
            var securedBytes = _pdfSecurity.EncryptPdfWithPassword(watermarked, userPass, ownerPass);

            var secureDir = SecurePrintsPath.GetSecureDir(tenantId);
            Directory.CreateDirectory(secureDir);
            var securePath = Path.Combine(secureDir, $"{jobId}.pdf");
            await System.IO.File.WriteAllBytesAsync(securePath, securedBytes);

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _printLogging.LogPrintAsync(tenantId, user.ShopId.Value, request.BookId, copies, user.Id, user.UserName, pageSummary);

            _logger.LogInformation("Print logged: Job={JobId}, Shop={ShopId}, Book={BookId}, Copies={Copies}, Pages={Pages}, IP={IP}",
                jobId, user.ShopId, request.BookId, copies, pageSummary, ipAddress);

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
                pages = pageSummary,
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

        var tenantId = await _apiKeys.ResolveTenantAsync(key);
        var shopId = await _apiKeys.ResolveShopAsync(key);     // 0 = tenant-wide key
        AgentStatusTracker.RecordHeartbeat(HashAgentKey(key), request?.Printers ?? new(), tenantId, shopId);
        return Ok(new { success = true });
    }

    [HttpGet("print-agent/status")]
    [AllowAnonymous]
    public async Task<IActionResult> AgentStatus()
    {
        // Show the printers of the caller's OWN agent(s) — the website must list
        // exactly what the shop's agent reads right now, never another agent's list.
        // A shop user sees only the agent(s) bound to their shop via the API key;
        // a teacher/admin sees the whole tenant's agents.
        var tenantId = _tenantContext.TenantId;
        int? shopId = null;

        var appUser = await _userManager.GetUserAsync(User);
        if (appUser != null)
        {
            if (appUser.TenantId.HasValue)
                tenantId = appUser.TenantId.Value;
            // Single-agent mode serves every shop from one device — no shop scoping.
            if (!ServeAllTenants && appUser.ShopId.HasValue
                && !await _userManager.IsInRoleAsync(appUser, "Teacher")
                && !await _userManager.IsInRoleAsync(appUser, "SystemAdmin"))
                shopId = appUser.ShopId.Value;
        }

        if (tenantId <= 0)
        {
            var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (!string.IsNullOrEmpty(key))
            {
                tenantId = await _apiKeys.ResolveTenantAsync(key);
                shopId = await _apiKeys.ResolveShopAsync(key);
                if (shopId == 0) shopId = null;
            }
        }

        if (tenantId <= 0)
            return Ok(new { connected = false, stale = false, lastSeen = (string?)null, tenantId = 0, shopId = 0, printers = new List<AgentPrinterInfo>() });

        // Authoritative source: the database, which every agent heartbeat updates.
        // The in-memory tracker is lost on restart and is per-instance, so it can
        // show a stale or empty list — the DB can't.
        var currentPrinters = await _printerRegistration.GetCurrentPrintersAsync(tenantId, shopId);
        var newest = currentPrinters.Count > 0 ? currentPrinters.Max(p => p.LastSeen) : (DateTime?)null;
        var ageSeconds = newest.HasValue ? (DateTime.UtcNow - newest.Value).TotalSeconds : double.MaxValue;

        return Ok(new
        {
            connected = ageSeconds < OfflineAfterSeconds,
            stale = ageSeconds >= StaleAfterSeconds && ageSeconds < OfflineAfterSeconds,
            lastSeen = newest?.ToString("O"),
            tenantId,
            shopId = shopId ?? 0,
            printers = currentPrinters.Select(p => new AgentPrinterInfo
            {
                Name = p.Name,
                Port = p.Port,
                ConnectionType = p.ConnectionType,
                Driver = p.Driver,
                Location = p.Location,
                Comment = p.Comment,
                IsDefault = p.IsDefault,
                IsOnline = p.IsOnline,
                Status = p.Status
            }).ToList()
        });
    }

    private static string HashAgentKey(string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes);
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

    [HttpPost("print-agent/test")]
    [AllowAnonymous]
    public async Task<IActionResult> TestApiKey()
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenantId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveTenantAsync(key) : 0;
        
        if (tenantId == 0)
            return Unauthorized(new { error = "Invalid API key" });

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        return Ok(new { 
            success = true, 
            message = "API key is valid",
            tenantId = tenantId,
            tenantName = tenant?.Name ?? "Unknown"
        });
    }

    [HttpPost("print-agent/register-printers")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterPrinters([FromBody] AgentHeartbeatRequest request)
    {
        var key = HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        var tenantId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveTenantAsync(key) : 0;
        var shopId = !string.IsNullOrEmpty(key) ? await _apiKeys.ResolveShopAsync(key) : 0;

        if (tenantId == 0)
            return Unauthorized(new { error = "Valid API key required." });

        int? shopScope = shopId == 0 ? null : shopId;   // 0 = tenant-wide key
        await _printerRegistration.RegisterPrintersAsync(key, shopScope, request.Printers);

        // Also update the in-memory tracker (authoritative source for the website)
        AgentStatusTracker.RecordHeartbeat(HashAgentKey(key), request.Printers, tenantId, shopId);

        return Ok(new { success = true, message = $"Registered {request.Printers.Count} printers" });
    }

    [HttpGet("printers")]
    public async Task<IActionResult> GetTenantPrinters()
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId <= 0)
            return Unauthorized(new { error = "Invalid tenant context" });

        var printers = await _printerRegistration.GetTenantPrintersAsync(tenantId);
        
        return Ok(new { 
            printers = printers.Select(p => new {
                name = p.Name,
                port = p.Port,
                connectionType = p.ConnectionType,
                driver = p.Driver,
                location = p.Location,
                comment = p.Comment,
                isDefault = p.IsDefault,
                isOnline = p.IsOnline,
                status = p.Status,
                lastSeen = p.LastSeen
            })
        });
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

public static class ViewCachePath
{
    /// <summary>Per-tenant folder holding watermarked view copies (inside the App_Data volume).</summary>
    public static string GetViewDir(int tenantId)
        => Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ViewCache", tenantId.ToString());

    /// <summary>
    /// Best-effort cleanup of stale view-cache entries (other days' files and leftover .tmp
    /// files from interrupted writes). The directory is dedicated to view-cache files, so any
    /// file not carrying today's day key is stale.
    /// </summary>
    public static void PruneStaleViews(string cacheDir, string keepDayKey)
    {
        try
        {
            if (!Directory.Exists(cacheDir)) return;
            foreach (var file in Directory.GetFiles(cacheDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || !name.Contains(keepDayKey, StringComparison.Ordinal))
                {
                    try { System.IO.File.Delete(file); } catch { /* best-effort */ }
                }
            }
        }
        catch { /* best-effort */ }
    }
}

public class ProcessPrintRequest
{
    public int BookId { get; set; }

    [Range(1, 50, ErrorMessage = "Copies must be between 1 and 50.")]
    public int Copies { get; set; } = 1;

    /// <summary>Page selection: null/empty/"all" = every page, otherwise e.g. "1-5, 8, 11-13".</summary>
    public string? Pages { get; set; }

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
    // The agent heartbeats every 3s. Treat <30s as fully connected,
    // 30–60s as stale (agent reachable moments ago, keep last known printers),
    // and >60s as offline.
    private const double OfflineAfterSeconds = 60;
    private const double StaleAfterSeconds = 30;
    // Forget agents that stopped heartbeating after 10 minutes.
    private static readonly TimeSpan AgentLifetime = TimeSpan.FromMinutes(10);

    // One slot PER AGENT, keyed by a hash of the agent's API key. Each agent has its
    // own key (one key per shop), so agents never overwrite each other. A slot holds
    // ONLY the printers the agent reported in its latest heartbeat — the website shows
    // exactly what that agent reads right now, never a union or history of old reads.
    private sealed class AgentState
    {
        public int TenantId;
        public int ShopId;                       // 0 = tenant-wide agent (legacy key)
        public DateTime LastSeen = DateTime.MinValue;
        public List<AgentPrinterInfo> Printers = new();
    }

    private static readonly Dictionary<string, AgentState> _agents = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    public static void RecordHeartbeat(string agentKeyHash, List<AgentPrinterInfo> printers, int tenantId, int shopId = 0)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _agents[agentKeyHash] = new AgentState
            {
                TenantId = tenantId,
                ShopId = shopId,
                LastSeen = now,
                Printers = printers ?? new()
            };
            foreach (var k in _agents.Where(kv => now - kv.Value.LastSeen > AgentLifetime).Select(kv => kv.Key).ToList())
                _agents.Remove(k);
        }
    }

    /// <summary>
    /// Latest heartbeat among the agents matching the given scope.
    /// Shop users see only THEIR shop's agent; teachers see the whole tenant.
    /// </summary>
    private static AgentState? Latest(int? tenantId, int? shopId)
    {
        lock (_lock)
        {
            AgentState? best = null;
            var bestSeen = DateTime.MinValue;
            foreach (var s in _agents.Values)
            {
                if (tenantId.HasValue && s.TenantId != tenantId.Value) continue;
                if (shopId.HasValue && s.ShopId != shopId.Value) continue;
                if (s.LastSeen > bestSeen) { bestSeen = s.LastSeen; best = s; }
            }
            return best;
        }
    }

    public static object GetStatus(int? tenantId, int? shopId = null)
    {
        var state = Latest(tenantId, shopId);
        var ageSeconds = state == null ? double.MaxValue : (DateTime.UtcNow - state.LastSeen).TotalSeconds;
        return new
        {
            connected = ageSeconds < OfflineAfterSeconds,
            stale = ageSeconds >= StaleAfterSeconds && ageSeconds < OfflineAfterSeconds,
            lastSeen = state == null ? (string?)null : state.LastSeen.ToString("O"),
            tenantId = state?.TenantId ?? 0,
            shopId = state?.ShopId ?? 0,
            printers = state?.Printers ?? new List<AgentPrinterInfo>()
        };
    }

    /// <summary>
    /// Accuracy gate: checks a requested printer name against the CURRENT printers
    /// reported by the agents of the caller's scope. Returns true for empty names
    /// (agent prints to default), false when the name is not read by any agent of
    /// that scope right now, and null when no agent has ever reported printers
    /// (nothing to validate against, e.g. right after a server restart).
    /// Shop users are validated against THEIR OWN shop's agent only, so one shop
    /// can never select a printer that belongs to another shop's device.
    /// </summary>
    public static bool? HasPrinter(string? printerName, int? tenantId, int? shopId = null)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            return true;

        lock (_lock)
        {
            if (_agents.Count == 0)
                return null;

            bool anyKnown = false;
            foreach (var s in _agents.Values)
            {
                if (tenantId.HasValue && s.TenantId != tenantId.Value) continue;
                if (shopId.HasValue && s.ShopId != shopId.Value) continue;
                anyKnown = true;
                if (s.Printers.Any(p =>
                    string.Equals(p.Name?.Trim(), printerName.Trim(), StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return anyKnown ? false : (bool?)null;
        }
    }
}

public class AgentPrinterInfo
{
    public string Name { get; set; } = "";
    public string? ConnectionType { get; set; }
    public bool IsDefault { get; set; }
    public bool IsOnline { get; set; } = true;
    public string? Status { get; set; }
    public string? Port { get; set; }
    public string? Driver { get; set; }
    public string? Location { get; set; }
    public string? Comment { get; set; }
}

public class AgentHeartbeatRequest
{
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}