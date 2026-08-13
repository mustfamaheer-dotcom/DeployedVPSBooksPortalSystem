using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Models;
using PrintingBooksPortal.Services;

namespace PrintingBooksPortal.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext? _tenantContext;   // scoped; null in design-time
    private readonly bool _multiTenancy;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null, MultiTenancyOptions? multiTenancy = null)
        : base(options)
    {
        _tenantContext = tenantContext;
        _multiTenancy = multiTenancy?.Enabled ?? true;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<EducationalBoard> EducationalBoards => Set<EducationalBoard>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<ShopBookAssignment> ShopBookAssignments => Set<ShopBookAssignment>();
    public DbSet<PrintLog> PrintLogs => Set<PrintLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<RegisteredPrinter> RegisteredPrinters => Set<RegisteredPrinter>();

    // ── Tenant scoping on writes ──
    // Interactive circuits have no HttpContext; the tenant id is resolved from
    // the circuit's auth state (see TenantContext). Any entity with a TenantId
    // of 0 added inside a tenant scope is assigned the current tenant.
    private static readonly HashSet<string> TenantScopedTypes = new()
    {
        nameof(Shop), nameof(EducationalBoard), nameof(Book),
        nameof(ShopBookAssignment), nameof(PrintLog), nameof(SystemSetting), nameof(RegisteredPrinter)
    };

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantScoping();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantScoping();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantScoping()
    {
        if (!_multiTenancy || _tenantContext == null || _tenantContext.TenantId <= 0)
            return;

        var tenantId = _tenantContext.TenantId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;

            var typeName = entry.Entity.GetType().Name;
            if (!TenantScopedTypes.Contains(typeName))
                continue;

            var tenantIdProp = entry.Property("TenantId");
            int current = tenantIdProp.CurrentValue is int v ? v : 0;
            if (current == 0)
                tenantIdProp.CurrentValue = tenantId;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── existing config ──
        builder.Entity<ShopBookAssignment>()
            .HasIndex(a => new { a.ShopId, a.BookId })
            .IsUnique();

        builder.Entity<PrintLog>()
            .HasIndex(l => l.PrintedAt);

        builder.Entity<PrintLog>()
            .HasIndex(l => l.ShopId);

        builder.Entity<PrintLog>()
            .HasIndex(l => l.BookId);

        // ── new config ──
        builder.Entity<SystemSetting>()
            .HasIndex(s => new { s.TenantId, s.Key })
            .IsUnique();

        builder.Entity<TenantApiKey>()
            .HasIndex(k => k.KeyHash)
            .IsUnique();

        builder.Entity<TenantApiKey>()
            .HasIndex(k => k.TenantId);

        builder.Entity<TenantApiKey>()
            .HasIndex(k => k.ShopId);

        builder.Entity<PrintLog>()
            .HasIndex(l => l.TenantId);

        builder.Entity<Book>()
            .HasIndex(b => b.TenantId);

        builder.Entity<Shop>()
            .HasIndex(s => s.TenantId);

        builder.Entity<EducationalBoard>()
            .HasIndex(b => b.TenantId);

        builder.Entity<ShopBookAssignment>()
            .HasIndex(a => a.TenantId);

        builder.Entity<SystemSetting>()
            .HasIndex(s => s.TenantId);

        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.TenantId);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Tenant).WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);

        // ── RegisteredPrinter configuration ──
        builder.Entity<RegisteredPrinter>()
            .HasIndex(p => new { p.TenantId, p.ShopId, p.AgentKeyHash, p.Name })
            .IsUnique();

        builder.Entity<RegisteredPrinter>()
            .HasIndex(p => p.TenantId);

        builder.Entity<RegisteredPrinter>()
            .HasIndex(p => p.ShopId);

        builder.Entity<RegisteredPrinter>()
            .HasIndex(p => p.AgentKeyHash);

        builder.Entity<RegisteredPrinter>()
            .HasIndex(p => p.LastSeen);

        // ── global query filters (multi-tenancy on) ──
        // SystemAdmin (no TenantId) sees ALL tenants; regular users see only
        // their own (fail-closed: unauthenticated → TenantId 0 → nothing).
        if (_multiTenancy && _tenantContext != null)
        {
            builder.Entity<Shop>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<EducationalBoard>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<Book>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<ShopBookAssignment>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<PrintLog>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<SystemSetting>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
            builder.Entity<RegisteredPrinter>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId || _tenantContext.IsSystemAdmin);
        }
    }
}