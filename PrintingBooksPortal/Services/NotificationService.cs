using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class NotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task NotifyAsync(string studentUserId, string title, string message, NotificationType type, string? relatedUrl = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StudentNotifications.Add(new StudentNotification
        {
            StudentUserId = studentUserId,
            Title = title,
            Message = message,
            Type = type,
            RelatedUrl = relatedUrl,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        });
        await db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(string studentUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StudentNotifications
            .CountAsync(n => n.StudentUserId == studentUserId && !n.IsRead);
    }

    public async Task<List<StudentNotification>> GetRecentAsync(string studentUserId, int count = 20)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.StudentNotifications
            .Where(n => n.StudentUserId == studentUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task MarkAllReadAsync(string studentUserId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unread = await db.StudentNotifications
            .Where(n => n.StudentUserId == studentUserId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.IsRead = true;
        }
        
        if (unread.Any())
        {
            await db.SaveChangesAsync();
        }
    }

    public async Task MarkReadAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = await db.StudentNotifications.FindAsync(id);
        if (n != null && !n.IsRead)
        {
            n.IsRead = true;
            await db.SaveChangesAsync();
        }
    }
}
