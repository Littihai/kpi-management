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
}
