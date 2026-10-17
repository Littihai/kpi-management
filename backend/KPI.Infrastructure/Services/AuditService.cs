using KPI.Application.Audit;
using KPI.Application.Common;
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

    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query)
    {
        var page = PagedResult<AuditLogDto>.ClampPage(query.Page);
        var size = PagedResult<AuditLogDto>.ClampSize(query.PageSize);

        var rows = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Entity)) rows = rows.Where(a => a.EntityName == query.Entity);
        if (!string.IsNullOrWhiteSpace(query.Action)) rows = rows.Where(a => a.Action == query.Action);
        if (query.UserId is { } userId) rows = rows.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(query.Ip)) rows = rows.Where(a => a.IpAddress == query.Ip);
        if (query.From is { } from) rows = rows.Where(a => a.CreatedAt >= from);
        if (query.To is { } to) rows = rows.Where(a => a.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(a =>
                a.Action.ToLower().Contains(term)
                || (a.UserEmail != null && a.UserEmail.ToLower().Contains(term))
                || (a.UserName != null && a.UserName.ToLower().Contains(term))
                || (a.NewValues != null && a.NewValues.ToLower().Contains(term)));
        }

        var total = await rows.CountAsync();
        var items = await rows
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(ToDtoExpression)
            .ToListAsync();

        return new PagedResult<AuditLogDto>(items, total, page, size);
    }

    public async Task<List<AuditLogDto>> ForEntityAsync(string entityName, string entityId) =>
        await _context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == entityName && a.EntityId == entityId)
            .OrderBy(a => a.CreatedAt)
            .Select(ToDtoExpression)
            .ToListAsync();

    private static readonly System.Linq.Expressions.Expression<Func<AuditLog, AuditLogDto>> ToDtoExpression =
        a => new AuditLogDto(
            a.Id, a.Action, a.EntityName, a.EntityId, a.OldValues, a.NewValues,
            a.UserId, a.UserEmail, a.UserName, a.UserRole, a.DepartmentName,
            a.IpAddress, a.UserAgent, a.CreatedAt);
}
