using System.Text.Json;
using KPI.Application.Audit;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;

    public AuditService(AppDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string action, string entityName, string entityId,
        string? oldValues = null, string? newValues = null,
        Guid? userId = null, string? userEmail = null)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues,
            UserId = userId,
            UserEmail = userEmail
        });
        await _context.SaveChangesAsync();
    }

    public async Task<List<AuditLogDto>> GetLogsAsync(
        string? entityName = null, Guid? userId = null)
    {
        var query = _context.AuditLogs.AsQueryable();

        if (!string.IsNullOrEmpty(entityName))
            query = query.Where(a => a.EntityName == entityName);

        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .Select(a => new AuditLogDto(
                a.Id, a.Action, a.EntityName, a.EntityId,
                a.OldValues, a.NewValues, a.UserEmail, a.CreatedAt))
            .ToListAsync();
    }
}