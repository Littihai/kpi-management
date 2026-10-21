using KPI.Application.Admin;
using KPI.Application.Audit;
using KPI.Application.Common;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Services;

public class OverviewService : IOverviewService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public OverviewService(AppDbContext db, IAuditService audit, TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _time = time;
    }

    public async Task<OverviewDto> GetAsync(int days)
    {
        days = Math.Clamp(days, 1, 90);
        var now = _time.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-days);

        // ── People ──
        var usersByRole = await _db.Users.AsNoTracking()
            .GroupBy(u => u.Role.Name)
            .Select(g => new { Role = g.Key, Count = g.Count(), Active = g.Count(u => u.IsActive) })
            .ToListAsync();
        var users = new UserStats(
            usersByRole.Sum(r => r.Count),
            usersByRole.Sum(r => r.Active),
            usersByRole.Sum(r => r.Count - r.Active),
            usersByRole.OrderByDescending(r => r.Count).Select(r => new CountByLabel(r.Role, r.Count)).ToList());

        // ── Requests (one aggregate query) ──
        var requests = _db.RequestLogs.AsNoTracking().Where(r => r.CreatedAt >= since);
        var stats = await requests
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Writes = g.Count(r => r.Method != "GET"),
                Unauthorized = g.Count(r => r.StatusCode == 401),
                Forbidden = g.Count(r => r.StatusCode == 403),
                Errors = g.Count(r => r.StatusCode >= 500),
                FailedLogins = g.Count(r => r.Path == "/api/auth/login" && r.StatusCode >= 400),
                Ips = g.Select(r => r.IpAddress).Distinct().Count(),
            })
            .FirstOrDefaultAsync();
        var requestStats = stats is null
            ? new RequestStats(0, 0, 0, 0, 0, 0, 0)
            : new RequestStats(stats.Total, stats.Writes, stats.Unauthorized, stats.Forbidden,
                stats.Errors, stats.FailedLogins, stats.Ips);

        var topIps = await requests
            .Where(r => r.IpAddress != null)
            .GroupBy(r => r.IpAddress!)
            .Select(g => new { Ip = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(8)
            .ToListAsync();

        var security = await requests
            .Where(r => r.StatusCode == 401 || r.StatusCode == 403 || r.StatusCode == 429
                        || (r.Path == "/api/auth/login" && r.StatusCode >= 400))
            .OrderByDescending(r => r.CreatedAt)
            .Take(15)
            .Select(ToRequestDto)
            .ToListAsync();

        var trendRows = await requests
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count(), Failures = g.Count(r => r.StatusCode >= 400) })
            .OrderBy(g => g.Day)
            .ToListAsync();
        var trend = trendRows.Select(t => new DailyPoint(t.Day.ToString("yyyy-MM-dd"), t.Count, t.Failures)).ToList();

        // ── Issues ──
        var issues = _db.Issues.AsNoTracking();
        var byStatus = await issues.GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var open = byStatus.Where(x => IssueStateMachine.IsOpen(x.Status)).Sum(x => x.Count);
        var overdue = await issues.CountAsync(IssueFilters.Overdue(now));

        var resolvedPairs = await issues
            .Where(i => i.ResolvedAt != null && i.ResolvedAt >= since)
            .Select(i => new { i.CreatedAt, ResolvedAt = i.ResolvedAt!.Value })
            .Take(5000)
            .ToListAsync();
        double? average = resolvedPairs.Count == 0
            ? null
            : Math.Round(resolvedPairs.Average(p => (p.ResolvedAt - p.CreatedAt).TotalHours), 1);

        var byDepartment = await issues
            .GroupBy(i => i.Department != null ? i.Department.Name : "ไม่ระบุแผนก")
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(8).ToListAsync();
        var byModule = await issues.GroupBy(i => i.Module)
            .Select(g => new { Module = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ToListAsync();

        var issueOverview = new IssueOverview(
            byStatus.Sum(x => x.Count),
            open,
            overdue,
            resolvedPairs.Count,
            average,
            byStatus.OrderByDescending(x => x.Count).Select(x => new CountByLabel(x.Status.ToString(), x.Count)).ToList(),
            byDepartment.Select(x => new CountByLabel(x.Label, x.Count)).ToList(),
            byModule.Select(x => new CountByLabel(x.Module.ToString(), x.Count)).ToList());

        // ── Recent activity ──
        var activity = (await _audit.SearchAsync(new AuditQuery(PageSize: 30))).Items;

        return new OverviewDto(
            days, now, users, requestStats, issueOverview,
            topIps.Select(i => new CountByLabel(i.Ip, i.Count)).ToList(),
            security, activity, trend,
            Online: []); // filled in by the controller from the live connection list
    }

    public async Task<PagedResult<RequestLogDto>> RequestLogsAsync(RequestLogQuery query)
    {
        var page = PagedResult<RequestLogDto>.ClampPage(query.Page);
        var size = PagedResult<RequestLogDto>.ClampSize(query.PageSize);

        var rows = _db.RequestLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Ip)) rows = rows.Where(r => r.IpAddress == query.Ip);
        if (query.UserId is { } userId) rows = rows.Where(r => r.UserId == userId);
        if (query.MinStatus is { } min) rows = rows.Where(r => r.StatusCode >= min);
        if (!string.IsNullOrWhiteSpace(query.Method)) rows = rows.Where(r => r.Method == query.Method!.ToUpper());
        if (query.From is { } from) rows = rows.Where(r => r.CreatedAt >= from);
        if (query.To is { } to) rows = rows.Where(r => r.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(r => r.Path.ToLower().Contains(term)
                                   || (r.UserEmail != null && r.UserEmail.ToLower().Contains(term)));
        }

        var total = await rows.CountAsync();
        var items = await rows
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(ToRequestDto)
            .ToListAsync();

        return new PagedResult<RequestLogDto>(items, total, page, size);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Domain.Entities.RequestLog, RequestLogDto>> ToRequestDto =
        r => new RequestLogDto(r.Id, r.CreatedAt, r.UserId, r.UserEmail, r.UserRole,
            r.Method, r.Path, r.StatusCode, r.DurationMs, r.IpAddress, r.UserAgent);
}
