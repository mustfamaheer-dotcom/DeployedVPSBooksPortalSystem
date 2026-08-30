using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task NotifyAsync(string studentUserId, string title, string message, NotificationType type, string? relatedUrl = null)
    {
        _db.StudentNotifications.Add(new StudentNotification
        {
            StudentUserId = studentUserId,
            Title = title,
            Message = message,
            Type = type,
            RelatedUrl = relatedUrl,
            CreatedAt = DateTime.UtcNow,
            IsRead = false
        });
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(string studentUserId)
    {
        return await _db.StudentNotifications
            .CountAsync(n => n.StudentUserId == studentUserId && !n.IsRead);
    }

    public async Task<List<StudentNotification>> GetRecentAsync(string studentUserId, int count = 20)
    {
        return await _db.StudentNotifications
            .Where(n => n.StudentUserId == studentUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task MarkAllReadAsync(string studentUserId)
    {
        var unread = await _db.StudentNotifications
            .Where(n => n.StudentUserId == studentUserId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.IsRead = true;
        }
        
        if (unread.Any())
        {
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkReadAsync(int id)
    {
        var n = await _db.StudentNotifications.FindAsync(id);
        if (n != null && !n.IsRead)
        {
            n.IsRead = true;
            await _db.SaveChangesAsync();
        }
    }
}
