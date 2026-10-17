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

    public TimeSpan ResolveFor(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => ResolveCritical,
        IssuePriority.High => ResolveHigh,
        IssuePriority.Medium => ResolveMedium,
        _ => ResolveLow,
    };

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
