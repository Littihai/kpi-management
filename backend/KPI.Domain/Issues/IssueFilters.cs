using System.Linq.Expressions;
using KPI.Domain.Entities;

namespace KPI.Domain.Issues;

/// <summary>
/// "Open" and "overdue" as database filters. The same rules exist in memory on <see cref="IssueStateMachine"/>
/// (for rows that are already loaded); a test keeps the two in step, so a list, a count and a detail page
/// can never disagree about whether an issue is late.
/// </summary>
public static class IssueFilters
{
    /// <summary>Not yet resolved, closed or rejected.</summary>
    public static readonly Expression<Func<Issue, bool>> Open = i =>
        i.Status != IssueStatus.Resolved && i.Status != IssueStatus.Closed && i.Status != IssueStatus.Rejected;

    public static readonly Expression<Func<Issue, bool>> Finished = i =>
        i.Status == IssueStatus.Resolved || i.Status == IssueStatus.Closed || i.Status == IssueStatus.Rejected;

    /// <summary>First response missed, or the resolve target passed while the clock is running.</summary>
    public static Expression<Func<Issue, bool>> Overdue(DateTime now) => i =>
        (i.FirstResponseAt == null && now > i.ResponseDueAt
            && (i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned))
        || ((i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned
             || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened)
            && now > i.ResolveDueAt);

    /// <summary>
    /// <see cref="Overdue"/> plus the issues inside the warning window before a deadline. The window is added to "now" rather
    /// than taken off the column, so the database can still use its indexes on the deadlines.
    /// </summary>
    public static Expression<Func<Issue, bool>> NearOrOverdue(DateTime now)
    {
        var responseEdge = now + IssueSlaPolicy.NearResponse;
        var resolveEdge = now + IssueSlaPolicy.NearResolve;
        return i =>
            (i.FirstResponseAt == null && responseEdge > i.ResponseDueAt
                && (i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned))
            || ((i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned
                 || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened)
                && resolveEdge > i.ResolveDueAt);
    }

    /// <summary>
    /// One <see cref="Attention"/> rule as a filter, so a list holds exactly the issues the badge counts (a test keeps them
    /// in step). A rule that cannot apply to this person (the queue rules for a requester) matches nothing.
    /// </summary>
    public static Expression<Func<Issue, bool>> AttentionMatches(IssueAttentionRule rule, Guid userId, bool queueWorker, DateTime now)
    {
        Expression<Func<Issue, bool>> none = i => false;
        return rule switch
        {
            IssueAttentionRule.NewInQueue => queueWorker
                ? i => i.Status == IssueStatus.New && i.AssigneeId == null
                : none,
            IssueAttentionRule.AssignedToMe => i => i.AssigneeId == userId
                && (i.Status == IssueStatus.Assigned || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened),
            IssueAttentionRule.Overdue => queueWorker ? Overdue(now) : none,
            IssueAttentionRule.WaitingForMe => i => i.ReporterUserId == userId && i.Status == IssueStatus.WaitingForReporter,
            IssueAttentionRule.ToConfirm => i => i.ReporterUserId == userId && i.Status == IssueStatus.Resolved,
            _ => queueWorker
                ? i => (i.Status == IssueStatus.New && i.AssigneeId == null)
                       || (i.AssigneeId == userId
                           && (i.Status == IssueStatus.Assigned || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened))
                       || (i.ReporterUserId == userId
                           && (i.Status == IssueStatus.WaitingForReporter || i.Status == IssueStatus.Resolved))
                       || (i.FirstResponseAt == null && now > i.ResponseDueAt
                           && (i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned))
                       || ((i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned
                            || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened)
                           && now > i.ResolveDueAt)
                : i => (i.AssigneeId == userId
                        && (i.Status == IssueStatus.Assigned || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened))
                       || (i.ReporterUserId == userId
                           && (i.Status == IssueStatus.WaitingForReporter || i.Status == IssueStatus.Resolved)),
        };
    }

    /// <summary>
    /// What is waiting for one person, issue by issue. Every flag is a database expression, so a count of all five
    /// (and of the issues that meet at least one) is a single query. The overdue test repeats <see cref="Overdue"/>
    /// because an expression cannot be nested inside another; a test keeps them, and <see cref="IssueStateMachine"/>, in step.
    /// </summary>
    /// <param name="userId">The person the flags are for.</param>
    /// <param name="queueWorker">Whether they work the IT queue (can manage issues). Only then do unclaimed and overdue issues wait for them.</param>
    public static Expression<Func<Issue, AttentionFlags>> Attention(Guid userId, bool queueWorker, DateTime now) => i => new AttentionFlags
    {
        NewInQueue = queueWorker && i.Status == IssueStatus.New && i.AssigneeId == null,
        AssignedToMe = i.AssigneeId == userId
            && (i.Status == IssueStatus.Assigned || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened),
        Overdue = queueWorker
            && ((i.FirstResponseAt == null && now > i.ResponseDueAt
                 && (i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned))
                || ((i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned
                     || i.Status == IssueStatus.InProgress || i.Status == IssueStatus.Reopened)
                    && now > i.ResolveDueAt)),
        WaitingForMe = i.ReporterUserId == userId && i.Status == IssueStatus.WaitingForReporter,
        ToConfirm = i.ReporterUserId == userId && i.Status == IssueStatus.Resolved,
    };
}

/// <summary>Which "waiting for you" rules one issue meets for one person; see <see cref="IssueFilters.Attention"/>.</summary>
public sealed class AttentionFlags
{
    /// <summary>New and nobody has picked it up (queue workers only).</summary>
    public bool NewInQueue { get; init; }

    /// <summary>Assigned to the person and still theirs to work on.</summary>
    public bool AssignedToMe { get; init; }

    /// <summary>Late against its response or resolve target (queue workers only).</summary>
    public bool Overdue { get; init; }

    /// <summary>The person reported it and IT is waiting for their answer.</summary>
    public bool WaitingForMe { get; init; }

    /// <summary>The person reported it and it is fixed, waiting for them to confirm.</summary>
    public bool ToConfirm { get; init; }
}
