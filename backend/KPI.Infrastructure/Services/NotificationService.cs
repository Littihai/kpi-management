using KPI.Application.Common;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Interfaces;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IUserRepository _users;
    private readonly TimeProvider _time;

    public NotificationService(AppDbContext db, IUserRepository users, TimeProvider time)
    {
        _db = db;
        _users = users;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, Guid userId)
    {
        var rows = await VisibleAsync(userId);
        if (query.UnreadOnly) rows = rows.Where(n => n.ReadAt == null);

        var page = PagedResult<NotificationDto>.ClampPage(query.Page);
        var size = PagedResult<NotificationDto>.ClampSize(query.PageSize);
        var total = await rows.CountAsync();
        var items = await rows
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(n => new { n.Id, n.IssueId, n.Kind, n.Title, n.Message, n.Level, n.CreatedAt, n.ReadAt })
            .ToListAsync();

        return new PagedResult<NotificationDto>(
            items.Select(n => new NotificationDto(n.Id, n.IssueId, n.Kind, n.Title, n.Message, n.Level,
                DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.ReadAt != null)).ToList(),
            total, page, size);
    }

    public async Task<int> UnreadCountAsync(Guid userId) =>
        await (await VisibleAsync(userId)).Where(n => n.ReadAt == null).Take(INotificationService.UnreadCap).CountAsync();

    public async Task MarkReadAsync(Guid id, Guid userId)
    {
        var row = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId)
                  ?? throw new NotFoundException("ไม่พบการแจ้งเตือน");
        if (row.ReadAt != null) return;
        row.ReadAt = Now;
        await _db.SaveChangesAsync();
    }

    public Task<int> MarkAllReadAsync(Guid userId) =>
        MarkUnreadAsync(_db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null));

    public Task<int> MarkIssueReadAsync(Guid issueId, Guid userId) =>
        MarkUnreadAsync(_db.Notifications.Where(n => n.UserId == userId && n.IssueId == issueId && n.ReadAt == null));

    public async Task<int> PurgeAsync(DateTime readCutoff, DateTime unreadCutoff)
    {
        var old = _db.Notifications.Where(n => (n.ReadAt != null && n.CreatedAt < readCutoff) || n.CreatedAt < unreadCutoff);
        if (_db.Database.IsRelational()) return await old.ExecuteDeleteAsync();

        var rows = await old.ToListAsync();
        _db.Notifications.RemoveRange(rows);
        await _db.SaveChangesAsync();
        return rows.Count;
    }

    /// <summary>The caller's entries, minus those about an issue they can no longer open.</summary>
    private async Task<IQueryable<Notification>> VisibleAsync(Guid userId)
    {
        var actor = await _users.GetByIdWithPermissionsAsync(userId);
        if (actor is null || !actor.IsActive || !actor.Role.RolePermissions.Any(rp => rp.Permission.Name == Permissions.Issue.View))
            throw new ForbiddenException("คุณไม่มีสิทธิ์ทำรายการนี้");

        var issues = IssueService.Scope(_db.Issues.AsNoTracking(), actor);
        return _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && (n.IssueId == null || issues.Any(i => i.Id == n.IssueId)));
    }

    private async Task<int> MarkUnreadAsync(IQueryable<Notification> unread)
    {
        var now = Now;
        if (_db.Database.IsRelational())
            return await unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));

        var rows = await unread.ToListAsync();
        foreach (var row in rows) row.ReadAt = now;
        await _db.SaveChangesAsync();
        return rows.Count;
    }
}
