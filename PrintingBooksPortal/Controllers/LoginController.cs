using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;
using System.Net;

namespace PrintingBooksPortal.Controllers;

[ApiController]
[Route("api")]
public class LoginController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoginController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpPost("login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login([FromForm] string email, [FromForm] string password, [FromForm] bool rememberMe)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
        if (!result.Succeeded)
            return Redirect("/login?error=" + Uri.EscapeDataString("Invalid email or password"));

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return Redirect("/");

        // Block deactivated tenant users at sign-in (§4.6)
        if (user.TenantId.HasValue)
        {
            var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.FindAsync(user.TenantId.Value);
            if (tenant == null || !tenant.IsActive)
            {
                // Use the dedicated signout endpoint to avoid "Headers read-only" on Blazor SSR
                var returnUrl = Uri.EscapeDataString("/login?error=" + Uri.EscapeDataString("Account is disabled. Contact your administrator."));
                return Redirect($"/api/signout?returnUrl={returnUrl}");
            }
        }

        // First login after seed: force password change before anything else (§4.8)
        if (user.MustChangePassword)
        {
            await _signInManager.SignInAsync(user, rememberMe);
            return Redirect("/account/change-password");
        }

        // Welcome popup after sign-in (§ UI): the first page loaded after login
        // shows a "Welcome back" confirmation toast. Names may be stored URL-encoded
        // (legacy data), so decode before escaping to avoid double-encoding artifacts.
        Response.Cookies.Append("bp_welcome", Uri.EscapeDataString(WebUtility.UrlDecode(user.FullName ?? user.UserName ?? "")),
            new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromMinutes(2) });

        if (await _userManager.IsInRoleAsync(user, "SystemAdmin")) return Redirect("/sa/dashboard");
        if (await _userManager.IsInRoleAsync(user, "Teacher"))    return Redirect("/admin/dashboard");
        if (await _userManager.IsInRoleAsync(user, "Shop"))       return Redirect("/shop/mybooks");
        return Redirect("/");
    }

    [HttpPost("change-password")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ChangePassword([FromForm] string currentPassword, [FromForm] string newPassword)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Redirect("/login");

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
            return Redirect("/account/change-password?error=" + Uri.EscapeDataString(string.Join("; ", result.Errors.Select(e => e.Description))));

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);

        // Redirect to the dedicated logout endpoint which handles SignOutAsync in its
        // own render cycle — avoids "Headers are read-only" when called from Blazor SSR.
        var returnUrl = Uri.EscapeDataString("/login?message=" + Uri.EscapeDataString("Password changed. Please sign in again."));
        return Redirect($"/api/signout?returnUrl={returnUrl}");
    }

    [HttpGet("signout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Signout([FromQuery] string? returnUrl)
    {
        await _signInManager.SignOutAsync();
        var destination = string.IsNullOrWhiteSpace(returnUrl) ? "/login" : returnUrl;
        return Redirect(destination);
    }
}