using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class StudentService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NotificationService _notifications;

    public StudentService(AppDbContext db, UserManager<ApplicationUser> userManager, NotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
    }

    public async Task<(ApplicationUser? User, IdentityResult Result)> RegisterStudentAsync(
        string fullName, string email, string password, List<int> tenantIds, string? studentNote)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsStudent = true,
            TenantId = tenantIds.FirstOrDefault() > 0 ? tenantIds.First() : 1 // arbitrary tenant for structural reasons, but enrollments dictate access
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            return (null, result);

        await _userManager.AddToRoleAsync(user, "Student");

        foreach (var tenantId in tenantIds)
        {
            _db.StudentEnrollments.Add(new StudentEnrollment
            {
                StudentUserId = user.Id,
                TenantId = tenantId,
                StudentNote = studentNote,
                Status = EnrollmentStatus.Pending
            });
        }

        await _db.SaveChangesAsync();
        return (user, result);
    }

    public async Task<List<StudentEnrollment>> ListPendingEnrollmentsAsync(int tenantId)
    {
        return await _db.StudentEnrollments
            .Include(e => e.Student)
            .Where(e => e.TenantId == tenantId && e.Status == EnrollmentStatus.Pending)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task<StudentEnrollment?> ApproveEnrollmentAsync(int enrollmentId, int tenantId)
    {
        var enrollment = await _db.StudentEnrollments.FindAsync(enrollmentId);
        if (enrollment == null || enrollment.TenantId != tenantId || enrollment.Status != EnrollmentStatus.Pending)
            return null;

        enrollment.Status = EnrollmentStatus.Approved;
        enrollment.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant != null)
        {
            await _notifications.NotifyAsync(enrollment.StudentUserId, "Enrollment Approved", 
                $"Your enrollment request for {tenant.Name} has been approved.", NotificationType.EnrollmentApproved);
        }

        return enrollment;
    }

    public async Task<StudentEnrollment?> RejectEnrollmentAsync(int enrollmentId, int tenantId, string reason)
    {
        var enrollment = await _db.StudentEnrollments.FindAsync(enrollmentId);
        if (enrollment == null || enrollment.TenantId != tenantId || enrollment.Status != EnrollmentStatus.Pending)
            return null;

        enrollment.Status = EnrollmentStatus.Rejected;
        enrollment.ReviewedAt = DateTime.UtcNow;
        enrollment.RejectionReason = reason;

        await _db.SaveChangesAsync();
        
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant != null)
        {
            await _notifications.NotifyAsync(enrollment.StudentUserId, "Enrollment Rejected", 
                $"Your enrollment request for {tenant.Name} has been rejected. Reason: {reason ?? "No reason provided."}", NotificationType.EnrollmentRejected);
        }

        return enrollment;
    }

    public async Task<StudentBookAccess?> GrantBookAccessAsync(string studentUserId, int bookId, int tenantId, string grantedByUserId)
    {
        var existing = await _db.StudentBookAccesses
            .FirstOrDefaultAsync(a => a.StudentUserId == studentUserId && a.BookId == bookId && a.TenantId == tenantId);

        if (existing != null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                existing.GrantedAt = DateTime.UtcNow;
                existing.GrantedByUserId = grantedByUserId;
                await _db.SaveChangesAsync();
            }
            return existing;
        }

        var access = new StudentBookAccess
        {
            StudentUserId = studentUserId,
            BookId = bookId,
            TenantId = tenantId,
            GrantedByUserId = grantedByUserId,
            IsActive = true
        };
        _db.StudentBookAccesses.Add(access);
        await _db.SaveChangesAsync();
        
        var book = await _db.Books.FindAsync(bookId);
        if (book != null)
        {
            await _notifications.NotifyAsync(studentUserId, "New Book Assigned", 
                $"You have been granted access to a new book: {book.Title}", NotificationType.BookGranted, $"/student/viewer/{bookId}");
        }

        return access;
    }

    public async Task<bool> RevokeBookAccessAsync(string studentUserId, int bookId, int tenantId)
    {
        var existing = await _db.StudentBookAccesses
            .FirstOrDefaultAsync(a => a.StudentUserId == studentUserId && a.BookId == bookId && a.TenantId == tenantId);

        if (existing == null || !existing.IsActive)
            return false;

        existing.IsActive = false;
        await _db.SaveChangesAsync();
        
        var book = await _db.Books.FindAsync(bookId);
        if (book != null)
        {
            await _notifications.NotifyAsync(studentUserId, "Book Access Revoked", 
                $"Your access to the book '{book.Title}' has been revoked.", NotificationType.BookRevoked);
        }

        return true;
    }

    public async Task<List<Tenant>> GetStudentTeachersAsync(string studentUserId)
    {
        return await _db.StudentEnrollments
            .Include(e => e.Tenant)
            .Where(e => e.StudentUserId == studentUserId && e.Status == EnrollmentStatus.Approved)
            .Select(e => e.Tenant)
            .ToListAsync();
    }

    public async Task<List<Book>> GetStudentBooksAsync(string studentUserId, int tenantId)
    {
        return await _db.StudentBookAccesses
            .Include(a => a.Book)
            .Where(a => a.StudentUserId == studentUserId && a.TenantId == tenantId && a.IsActive)
            .Select(a => a.Book)
            .ToListAsync();
    }
}
