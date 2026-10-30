namespace KPI.Domain.Issues;

/// <summary>Service-level targets. Calendar hours, not working hours.</summary>
public sealed record IssueSlaPolicy(
    TimeSpan FirstResponse,
    TimeSpan ResolveCritical,
    TimeSpan ResolveHigh,
    TimeSpan ResolveMedium,
    TimeSpan ResolveLow)
{
    /// <summary>First response within 2 hours; resolution targets 4 h / 1 day / 3 days / 7 days.</summary>
    public static readonly IssueSlaPolicy Default = new(
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(24),
        TimeSpan.FromHours(72),
        TimeSpan.FromHours(168));

    /// <summary>
    /// "About to be late": an unanswered issue within this long of its response target. A fixed window, not a share of the
    /// target, so it is simple to explain; the cost is that a Critical issue (4 hour target) is always inside the resolve window.
    /// </summary>
    public static readonly TimeSpan NearResponse = TimeSpan.FromMinutes(30);

    /// <summary>"About to be late": a running issue within this long of its resolve target.</summary>
    public static readonly TimeSpan NearResolve = TimeSpan.FromHours(4);

    public TimeSpan ResolveFor(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => ResolveCritical,
        IssuePriority.High => ResolveHigh,
        IssuePriority.Medium => ResolveMedium,
        _ => ResolveLow,
    };

    /// <summary>
    /// The resolve deadline after the priority changes: the same moment moved by the difference between the two windows.
    /// Recomputing "created + window" instead would drop the time already credited for waiting on the reporter and the
    /// fresh target a reopened issue was given, and could make the issue instantly overdue.
    /// </summary>
    public DateTime ShiftResolveDue(DateTime resolveDue, IssuePriority from, IssuePriority to) =>
        resolveDue + (ResolveFor(to) - ResolveFor(from));

    /// <summary>The deadline pushed back by the time the clock was paused (waiting for the reporter, or on hold).</summary>
    public static DateTime PushBackForPause(DateTime resolveDue, DateTime pausedSince, DateTime now) =>
        now > pausedSince ? resolveDue + (now - pausedSince) : resolveDue;

    public DateTime ResponseDue(DateTime createdAt) => createdAt + FirstResponse;
    public DateTime ResolveDue(DateTime createdAt, IssuePriority priority) => createdAt + ResolveFor(priority);

    /// <summary>The priority an issue starts with, from the reporter's own impact answer. IT can change it.</summary>
    public static IssuePriority InitialPriority(IssueImpact impact) => impact switch
    {
        IssueImpact.Blocking => IssuePriority.High,
        IssueImpact.Degraded => IssuePriority.Medium,
        _ => IssuePriority.Low,
    };
}
