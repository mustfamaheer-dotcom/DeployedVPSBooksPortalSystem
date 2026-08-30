using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services
{
    public class AuditService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        
        public AuditService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }
        
        public async Task LogAsync(int tenantId, string userId, string action, string entityType, string entityId, string details = "")
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var log = new AuditLog
            {
                TenantId = tenantId,
                ActorUserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                OccurredAt = DateTime.UtcNow
            };
            
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();
        }
        
        public async Task<List<AuditLog>> GetRecentLogsAsync(int count = 100)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            return await db.AuditLogs
                .OrderByDescending(a => a.OccurredAt)
                .Take(count)
                .ToListAsync();
        }
    }
}
