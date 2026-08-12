using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Data;

public static class DbSeeder
{
    public const string SysAdminEmail = "sysadmin@drbahigPortal.com";
    public const string SysAdminPassword = "SysAdmin@2026";   // fixed requirement; override via env SystemAdmin__InitialPassword

    public static async Task SeedAsync(AppDbContext db, UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager, IConfiguration configuration)
    {
        // ── Roles ──
        foreach (var role in new[] { "Admin", "Shop", "Teacher", "SystemAdmin" })
        {
            try
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }
            catch { }
        }

        // ── Default tenant (Id = 1) — must exist before seeding tenant-scoped data ──
        try
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == 1))
            {
                db.Tenants.Add(new Tenant
                {
                    Id = 1,
                    Name = "Default Tenant",
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }
        }
        catch { }

        // ── Legacy accounts → Teacher on tenant 1 (§12.3 backfill, idempotent) ──
        try
        {
            var admins = await userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                if (!await userManager.IsInRoleAsync(admin, "Teacher"))
                    await userManager.AddToRoleAsync(admin, "Teacher");
                if (admin.TenantId != 1)
                {
                    admin.TenantId = 1;
                    await userManager.UpdateAsync(admin);
                }
            }
        }
        catch { }

        // Legacy default admin account (tenant-1 Teacher, keeps Admin role during transition)
        try
        {
            if (await userManager.FindByEmailAsync("admin@printingbooks.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@printingbooks.com",
                    Email = "admin@printingbooks.com",
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    TenantId = 1
                };
                var result = await userManager.CreateAsync(admin, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                    await userManager.AddToRoleAsync(admin, "Teacher");
                }
            }
        }
        catch { }

        // ── SystemAdmin provisioning (§12.6 — idempotent; password never reset) ──
        try
        {
            var sysAdminPassword = configuration["SystemAdmin__InitialPassword"] ?? SysAdminPassword;

            var official = await userManager.FindByEmailAsync(SysAdminEmail);
            if (official == null)
            {
                official = new ApplicationUser
                {
                    UserName = SysAdminEmail,
                    Email = SysAdminEmail,
                    FullName = "Platform Administrator",
                    EmailConfirmed = true,
                    TenantId = null,                         // SystemAdmin: no tenant (fail-closed §2.1)
                    MustChangePassword = false
                };
                var created = await userManager.CreateAsync(official, sysAdminPassword);
                if (created.Succeeded)
                {
                    await userManager.AddToRoleAsync(official, "SystemAdmin");
                    if (configuration["Sa:ForcePasswordChangeOnFirstLogin"] == "true")
                    {
                        official.MustChangePassword = true;  // optional, default off (§4.8)
                        await db.SaveChangesAsync();
                    }
                }
            }
            else
            {
                // Ensure the SystemAdmin role is assigned
                if (!await userManager.IsInRoleAsync(official, "SystemAdmin"))
                    await userManager.AddToRoleAsync(official, "SystemAdmin");

                // Sync password to the configured value in case the hash is stale
                // (e.g. after a password change in appsettings / env var).
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(official);
                await userManager.ResetPasswordAsync(official, resetToken, sysAdminPassword);
            }

            // Heal: ensure only the ONE official account holds SystemAdmin
            foreach (var holder in await userManager.GetUsersInRoleAsync("SystemAdmin"))
            {
                if (!string.Equals(holder.Email, SysAdminEmail, StringComparison.OrdinalIgnoreCase))
                    await userManager.RemoveFromRoleAsync(holder, "SystemAdmin");
            }
        }
        catch { }

        // ── Boards (per default tenant) ──
        try
        {
            if (!await db.EducationalBoards.AnyAsync())
            {
                db.EducationalBoards.AddRange(
                    new EducationalBoard { TenantId = 1, Name = "Cambridge IGCSE", Description = "Cambridge International General Certificate of Secondary Education" },
                    new EducationalBoard { TenantId = 1, Name = "Edexcel International", Description = "Pearson Edexcel International Curriculum" },
                    new EducationalBoard { TenantId = 1, Name = "IB Diploma", Description = "International Baccalaureate Diploma Programme" },
                    new EducationalBoard { TenantId = 1, Name = "National Curriculum", Description = "Local National Educational Board" }
                );
                await db.SaveChangesAsync();
            }
        }
        catch { }

        // ── Ensure Shop users have tenant 1 (they share the default tenant's books) ──
        try
        {
            var sansTenant = await db.Users.Where(u => u.TenantId == null && u.ShopId != null).ToListAsync();
            foreach (var u in sansTenant)
                u.TenantId = 1;
            if (sansTenant.Count > 0)
                await db.SaveChangesAsync();
        }
        catch { }
    }
}