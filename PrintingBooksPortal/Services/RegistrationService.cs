using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class RegistrationService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApiKeyService _apiKeys;

    public RegistrationService(AppDbContext db, UserManager<ApplicationUser> userManager, IApiKeyService apiKeys)
    {
        _db = db;
        _userManager = userManager;
        _apiKeys = apiKeys;
    }

    public async Task<RegistrationRequest> SubmitTeacherRequestAsync(
        string applicantName, string email, string? phone, string organizationName, string? message, string password)
    {
        var request = new RegistrationRequest
        {
            Type = RegistrationType.Teacher,
            ApplicantName = applicantName,
            Email = email,
            Phone = phone,
            OrganizationName = organizationName,
            Message = message,
            StoredPassword = password, // Need cleartext available upon approval to create IdentityUser
            Status = RegistrationStatus.Pending
        };

        _db.RegistrationRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public async Task<RegistrationRequest> SubmitBookshopRequestAsync(
        string applicantName, string email, string? phone, string bookshopName, string? address, string? message, string password)
    {
        var request = new RegistrationRequest
        {
            Type = RegistrationType.Bookshop,
            ApplicantName = applicantName,
            Email = email,
            Phone = phone,
            OrganizationName = bookshopName,
            Address = address,
            Message = message,
            StoredPassword = password,
            Status = RegistrationStatus.Pending
        };

        _db.RegistrationRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public async Task<List<RegistrationRequest>> ListPendingAsync()
    {
        return await _db.RegistrationRequests
            .Where(r => r.Status == RegistrationStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<RegistrationRequest?> ApproveTeacherAsync(int requestId, string adminUserId)
    {
        var request = await _db.RegistrationRequests.FindAsync(requestId);
        if (request == null || request.Status != RegistrationStatus.Pending || request.Type != RegistrationType.Teacher)
            return null;

        // Create Tenant
        var tenant = new Tenant
        {
            Name = request.OrganizationName,
            IsActive = true
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(); // get ID

        // Create User
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.ApplicantName,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };

        var result = await _userManager.CreateAsync(user, request.StoredPassword ?? "TempPass123!");
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "Teacher");
        }

        // Update Request
        request.Status = RegistrationStatus.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByUserId = adminUserId;
        request.CreatedTenantId = tenant.Id;
        request.StoredPassword = null; // Clear it out for safety

        await _db.SaveChangesAsync();
        return request;
    }

    public async Task<(RegistrationRequest Request, string ApiKey)?> ApproveBookshopAsync(int requestId, string adminUserId)
    {
        var request = await _db.RegistrationRequests.FindAsync(requestId);
        if (request == null || request.Status != RegistrationStatus.Pending || request.Type != RegistrationType.Bookshop)
            return null;

        // A bookshop doesn't necessarily need a new tenant if we follow Option A (use default tenant 1).
        // Let's ensure tenant 1 is used.
        var defaultTenant = await _db.Tenants.FindAsync(1) ?? throw new InvalidOperationException("Default tenant not found.");

        // Create Shop
        var shop = new Shop
        {
            Name = request.OrganizationName,
            Address = request.Address,
            Phone = request.Phone,
            TenantId = defaultTenant.Id
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();

        // Create User
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.ApplicantName,
            EmailConfirmed = true,
            TenantId = defaultTenant.Id,
            ShopId = shop.Id
        };

        var result = await _userManager.CreateAsync(user, request.StoredPassword ?? "TempPass123!");
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "BookshopOwner");
        }

        // Create API Key
        var rawKey = _apiKeys.GenerateKey(defaultTenant.Id, shop.Id);

        // Update Request
        request.Status = RegistrationStatus.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByUserId = adminUserId;
        request.CreatedTenantId = defaultTenant.Id;
        request.CreatedShopId = shop.Id;
        request.StoredPassword = null;

        await _db.SaveChangesAsync();
        return (request, rawKey);
    }

    public async Task<RegistrationRequest?> RejectRequestAsync(int requestId, string adminUserId, string reason)
    {
        var request = await _db.RegistrationRequests.FindAsync(requestId);
        if (request == null || request.Status != RegistrationStatus.Pending)
            return null;

        request.Status = RegistrationStatus.Rejected;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByUserId = adminUserId;
        request.RejectionReason = reason;
        request.StoredPassword = null; 

        await _db.SaveChangesAsync();
        return request;
    }
}
