using KPI.Domain.Exceptions;

namespace KPI.Domain.Issues;

public enum IssueAction
{
    Assign,
    Start,
    RequestInfo,
    Resolve,
    Confirm,   // reporter (or IT) accepts the fix → Closed
    Reopen,
    Reject,

    /// <summary>Park the work (waiting for a vendor or a part). Pauses the resolve clock.</summary>
    Hold,

    /// <summary>Continue from WaitingForReporter or OnHold: the staff move that does not need the reporter's answer.</summary>
    Resume,
}

/// <summary>Which issue actions are valid in which status, and the status each leads to.</summary>
public static class IssueStateMachine
{
    /// <summary>Resolved issues the reporter does not answer are closed after this long.</summary>
    public static readonly TimeSpan AutoCloseAfter = TimeSpan.FromDays(3);

    /// <summary>How long after closing the reporter may still reopen an issue.</summary>
    public static readonly TimeSpan ReporterReopenWindow = TimeSpan.FromDays(7);

    /// <summary>How long after the fix the reporter may rate it.</summary>
    public static readonly TimeSpan FeedbackWindow = TimeSpan.FromDays(30);

    public static bool IsOpen(IssueStatus status) =>
        status is not (IssueStatus.Resolved or IssueStatus.Closed or IssueStatus.Rejected);

    /// <summary>The resolve clock runs only while the next move is IT's, not while waiting for the reporter.</summary>
    public static bool ClockRuns(IssueStatus status) =>
        status is IssueStatus.New or IssueStatus.Assigned or IssueStatus.InProgress or IssueStatus.Reopened;

    public static bool IsResponseOverdue(IssueStatus status, DateTime? firstResponseAt, DateTime responseDueAt, DateTime now) =>
        firstResponseAt is null && now > responseDueAt && (status is IssueStatus.New or IssueStatus.Assigned);

    public static bool IsResolveOverdue(IssueStatus status, DateTime resolveDueAt, DateTime now) =>
        ClockRuns(status) && now > resolveDueAt;

    /// <summary>Overdue, or inside the warning window before it (<see cref="IssueSlaPolicy.NearResponse"/>, <see cref="IssueSlaPolicy.NearResolve"/>).</summary>
    public static bool IsNearOrOverdue(IssueStatus status, DateTime? firstResponseAt, DateTime responseDueAt, DateTime resolveDueAt, DateTime now) =>
        IsResponseOverdue(status, firstResponseAt, responseDueAt, now)
        || IsResolveOverdue(status, resolveDueAt, now)
        || (firstResponseAt is null && now + IssueSlaPolicy.NearResponse > responseDueAt && (status is IssueStatus.New or IssueStatus.Assigned))
        || (ClockRuns(status) && now + IssueSlaPolicy.NearResolve > resolveDueAt);

    /// <summary>A fix can be rated once it is resolved (or closed after being resolved), for <see cref="FeedbackWindow"/>.</summary>
    public static bool CanGiveFeedback(IssueStatus status, DateTime? resolvedAt, DateTime now) =>
        status is IssueStatus.Resolved or IssueStatus.Closed && resolvedAt is { } at && now - at <= FeedbackWindow;

    public static bool CanApply(IssueStatus status, IssueAction action) => action switch
    {
        IssueAction.Assign => IsOpen(status),
        IssueAction.Start => status is IssueStatus.New or IssueStatus.Assigned or IssueStatus.Reopened,
        IssueAction.RequestInfo => status is IssueStatus.Assigned or IssueStatus.InProgress or IssueStatus.Reopened,
        IssueAction.Resolve => status is IssueStatus.Assigned or IssueStatus.InProgress or IssueStatus.Reopened,
        IssueAction.Confirm => status == IssueStatus.Resolved,
        IssueAction.Reopen => status is IssueStatus.Resolved or IssueStatus.Closed or IssueStatus.Rejected,
        IssueAction.Reject => IsOpen(status),
        IssueAction.Hold => status is IssueStatus.Assigned or IssueStatus.InProgress or IssueStatus.Reopened,
        IssueAction.Resume => status is IssueStatus.WaitingForReporter or IssueStatus.OnHold,
        _ => false,
    };

    /// <summary>The status after the action. <c>Assign</c> only moves a brand-new issue.</summary>
    public static IssueStatus Next(IssueStatus current, IssueAction action) => action switch
    {
        IssueAction.Assign => current == IssueStatus.New ? IssueStatus.Assigned : current,
        IssueAction.Start => IssueStatus.InProgress,
        IssueAction.RequestInfo => IssueStatus.WaitingForReporter,
        IssueAction.Resolve => IssueStatus.Resolved,
        IssueAction.Confirm => IssueStatus.Closed,
        IssueAction.Reopen => IssueStatus.Reopened,
        IssueAction.Reject => IssueStatus.Rejected,
        IssueAction.Hold => IssueStatus.OnHold,
        IssueAction.Resume => IssueStatus.InProgress,
        _ => current,
    };

    public static void EnsureCanApply(IssueStatus status, IssueAction action)
    {
        if (!CanApply(status, action))
            throw new InvalidStateException($"Action '{action}' is not allowed while the issue is {status}.");
    }

    /// <summary>Colour band the UI uses for a status.</summary>
    public static string Tone(IssueStatus status) => status switch
    {
        IssueStatus.New or IssueStatus.Reopened => "orange",
        IssueStatus.Assigned or IssueStatus.InProgress => "blue",
        IssueStatus.WaitingForReporter or IssueStatus.OnHold => "amber",
        IssueStatus.Resolved => "green",
        _ => "gray",
    };
}
