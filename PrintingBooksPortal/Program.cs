using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Fonts;
using PrintingBooksPortal.Components;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Hubs;
using PrintingBooksPortal.Middleware;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

var builder = WebApplication.CreateBuilder(args);

// Persist Data Protection keys to disk so antiforgery tokens and auth cookies
// survive container restarts.  Without this, every restart generates a new key
// ring and all old cookies become invalid (Blazor circuit auth fails).
var dataProtectionPath = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName("BooksPortal");

// PdfSharpCore has no default fonts on Linux containers; resolve Arial and
// friends from the fonts installed in the image (see Dockerfile: fonts-liberation).
GlobalFontSettings.FontResolver = new PdfFontResolver();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var isProduction = builder.Environment.IsProduction();
var multiTenancyEnabled = builder.Configuration.GetValue<bool?>("MultiTenancy:Enabled") ?? true;

builder.Services.AddSingleton(new MultiTenancyOptions { Enabled = multiTenancyEnabled }); // AppDbContext ctor param

void ConfigureDbContext(DbContextOptionsBuilder options, string cs)
{
    if (isProduction)
        options.UseSqlServer(cs);
    else
        options.UseSqlite(cs);
}

builder.Services.AddDbContext<AppDbContext>(options => ConfigureDbContext(options, connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddClaimsPrincipalFactory<TenantClaimsPrincipalFactory>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
    options.Cookie.HttpOnly = true;
    // Security: HTTPS-only cookies by default. Can be relaxed to "SameAsRequest"
    // for HTTP-only staging (e.g. IP-based deployment before the domain + SSL is attached).
    var cookieSecurePolicy = builder.Configuration.GetValue<CookieSecurePolicy?>("Security:CookieSecurePolicy")
                             ?? CookieSecurePolicy.Always;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Strict;                // Security: prevent CSRF
    options.Cookie.IsEssential = true;                            // Security: GDPR-compliant auth
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",   policy => policy.RequireRole("Admin")); // legacy — see §4.5 transition
    options.AddPolicy("ShopOnly",    policy => policy.RequireRole("Shop"));
    options.AddPolicy("SystemAdminOnly", policy => policy.RequireRole("SystemAdmin"));
    options.AddPolicy("TenantAdmin",     policy => policy.RequireRole("Teacher", "SystemAdmin"));
    options.AddPolicy("TenantUser",      policy => policy.RequireRole("Teacher", "Shop"));
});

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<PrintLoggingService>();
builder.Services.AddScoped<IWatermarkService, WatermarkService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddSingleton<PrintTokenService>();
builder.Services.AddSingleton<IPdfSecurityService, PdfSecurityService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<SystemAdminService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<ServerAuthenticationMessageHandler>();
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<ServerAuthenticationMessageHandler>();
    handler.InnerHandler = new SocketsHttpHandler();   // must end the chain with a real handler
    return new HttpClient(handler)
    {
        BaseAddress = new Uri(builder.Configuration["AppUrl"] ?? "http://localhost:5035")
    };
});

var app = builder.Build();

// Security: trust X-Forwarded-Proto/X-Forwarded-Host from the reverse proxy (RunASP.NET load balancer)
// so that UseHttpsRedirection() and CookieSecurePolicy.Always work correctly behind SSL termination.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();   // Security: HTTP Strict-Transport-Security header
}
else
{
    app.UseDeveloperExceptionPage();
}

// Security: redirect HTTP → HTTPS. Skippable for HTTP-only staging
// (IP-based deployment before the domain + SSL certificate is attached).
var requireHttps = builder.Configuration.GetValue<bool?>("Security:RequireHttps") ?? true;
if (requireHttps)
    app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<TenantActivityMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapControllers();
app.MapHub<PrintHub>("/hubs/print");

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (isProduction)
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not apply migrations automatically. The SettingsService will auto-create the SystemSettings table on first access.");
        }
    }
    else
    {
        // Dev/SQLite: migrations are authored for SQL Server (AddTenantManagement needs AddServer features),
        // so build the schema directly from the model instead of replaying the SQL Server migration script.
        await db.Database.EnsureCreatedAsync();
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await DbSeeder.SeedAsync(db, userManager, roleManager, builder.Configuration);

    // Repair: users created before tenancy wiring had a wrong/missing TenantId,
    // which breaks tenant-scoped visibility (their shop's books never appear).
    // Inherit it from the user's shop. Idempotent; safe on both SQLite and SQL Server.
    var repaired = await db.Database.ExecuteSqlRawAsync(
        "UPDATE AspNetUsers SET TenantId = (SELECT s.TenantId FROM Shops s WHERE s.Id = AspNetUsers.ShopId) WHERE ShopId IS NOT NULL AND (TenantId IS NULL OR TenantId <> (SELECT s.TenantId FROM Shops s WHERE s.Id = AspNetUsers.ShopId))");
    if (repaired > 0)
        logger.LogInformation("Tenant-repaired {Count} users missing TenantId.", repaired);

    logger.LogInformation("Database initialization completed.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database initialization failed. The app will still start.");
}

app.Run();

public partial class Program { } // needed by WebApplicationFactory in tests