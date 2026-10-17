using KPI.Application.Audit;
using KPI.Application.Common;

namespace KPI.Application.Admin;

public interface IOverviewService
{
    /// <summary>System-wide picture for the last <paramref name="days"/> days (SuperAdmin only).</summary>
    Task<OverviewDto> GetAsync(int days);

    Task<PagedResult<RequestLogDto>> RequestLogsAsync(RequestLogQuery query);
}

/// <summary>Who is connected right now. Implemented by the SignalR hub.</summary>
public interface IPresenceTracker
{
    IReadOnlyList<OnlineUserDto> Snapshot();
}

public record OnlineUserDto(Guid UserId, string Name, string Role, string? IpAddress, int Connections, DateTime Since);

public record CountByLabel(string Label, int Count);

public record DailyPoint(string Day, int Requests, int Failures);

public record OverviewDto(
    int Days,
    DateTime GeneratedAt,
    UserStats Users,
    RequestStats Requests,
    IssueOverview Issues,
    KpiOverview Kpis,
    IReadOnlyList<CountByLabel> TopIps,
    IReadOnlyList<RequestLogDto> SecurityEvents,
    IReadOnlyList<AuditLogDto> Activity,
    IReadOnlyList<DailyPoint> Trend,
    IReadOnlyList<OnlineUserDto> Online);

public record UserStats(int Total, int Active, int Inactive, IReadOnlyList<CountByLabel> ByRole);

public record RequestStats(int Total, int Writes, int Unauthorized, int Forbidden, int Errors, int FailedLogins, int DistinctIps);

public record IssueOverview(
    int Total,
    int Open,
    int Overdue,
    int ResolvedInPeriod,
    double? AverageResolveHours,
    IReadOnlyList<CountByLabel> ByStatus,
    IReadOnlyList<CountByLabel> ByDepartment,
    IReadOnlyList<CountByLabel> ByModule);

public record KpiOverview(int Total, IReadOnlyList<CountByLabel> ByStatus);

public record RequestLogDto(
    Guid Id,
    DateTime CreatedAt,
    Guid? UserId,
    string? UserEmail,
    string? UserRole,
    string Method,
    string Path,
    int StatusCode,
    int DurationMs,
    string? IpAddress,
    string? UserAgent);

public record RequestLogQuery(
    string? Search = null,
    string? Ip = null,
    Guid? UserId = null,
    int? MinStatus = null,
    string? Method = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50);
