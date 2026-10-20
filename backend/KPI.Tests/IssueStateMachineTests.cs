using KPI.Domain.Exceptions;
using KPI.Domain.Issues;

namespace KPI.Tests;

public class IssueStateMachineTests
{
    // action → the only statuses it may start from
    private static readonly Dictionary<IssueAction, IssueStatus[]> Allowed = new()
    {
        [IssueAction.Assign] = [IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.WaitingForReporter, IssueStatus.Reopened, IssueStatus.OnHold],
        [IssueAction.Start] = [IssueStatus.New, IssueStatus.Assigned, IssueStatus.Reopened],
        [IssueAction.RequestInfo] = [IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.Reopened],
        [IssueAction.Resolve] = [IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.Reopened],
        [IssueAction.Confirm] = [IssueStatus.Resolved],
        [IssueAction.Reopen] = [IssueStatus.Resolved, IssueStatus.Closed, IssueStatus.Rejected],
        [IssueAction.Reject] = [IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.WaitingForReporter, IssueStatus.Reopened, IssueStatus.OnHold],
        [IssueAction.Hold] = [IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.Reopened],
        [IssueAction.Resume] = [IssueStatus.WaitingForReporter, IssueStatus.OnHold],
    };

    public static IEnumerable<object[]> EveryStatusAndAction() =>
        from status in Enum.GetValues<IssueStatus>()
        from action in Enum.GetValues<IssueAction>()
        select new object[] { status, action };

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void CanApply_matches_the_transition_table(IssueStatus status, IssueAction action) =>
        Assert.Equal(Allowed[action].Contains(status), IssueStateMachine.CanApply(status, action));

    [Theory]
    [InlineData(IssueStatus.New, IssueAction.Assign, IssueStatus.Assigned)]
    [InlineData(IssueStatus.InProgress, IssueAction.Assign, IssueStatus.InProgress)] // reassigning keeps the status
    [InlineData(IssueStatus.Assigned, IssueAction.Start, IssueStatus.InProgress)]
    [InlineData(IssueStatus.InProgress, IssueAction.Hold, IssueStatus.OnHold)]
    [InlineData(IssueStatus.OnHold, IssueAction.Resume, IssueStatus.InProgress)]
    [InlineData(IssueStatus.InProgress, IssueAction.RequestInfo, IssueStatus.WaitingForReporter)]
    [InlineData(IssueStatus.InProgress, IssueAction.Resolve, IssueStatus.Resolved)]
    [InlineData(IssueStatus.Resolved, IssueAction.Confirm, IssueStatus.Closed)]
    [InlineData(IssueStatus.Resolved, IssueAction.Reopen, IssueStatus.Reopened)]
    [InlineData(IssueStatus.New, IssueAction.Reject, IssueStatus.Rejected)]
    public void Next_gives_the_expected_status(IssueStatus from, IssueAction action, IssueStatus to) =>
        Assert.Equal(to, IssueStateMachine.Next(from, action));

    [Fact]
    public void Open_means_not_resolved_closed_or_rejected()
    {
        Assert.All([IssueStatus.New, IssueStatus.Assigned, IssueStatus.InProgress, IssueStatus.WaitingForReporter, IssueStatus.Reopened, IssueStatus.OnHold],
            s => Assert.True(IssueStateMachine.IsOpen(s)));
        Assert.All([IssueStatus.Resolved, IssueStatus.Closed, IssueStatus.Rejected],
            s => Assert.False(IssueStateMachine.IsOpen(s)));
    }

    [Fact]
    public void A_bad_transition_throws_InvalidStateException()
    {
        Assert.Throws<InvalidStateException>(() => IssueStateMachine.EnsureCanApply(IssueStatus.New, IssueAction.Resolve));
        IssueStateMachine.EnsureCanApply(IssueStatus.Assigned, IssueAction.Resolve);
    }

    // ── SLA ──

    [Fact]
    public void The_first_response_target_is_two_hours_and_resolution_targets_scale_with_priority()
    {
        var created = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
        var sla = IssueSlaPolicy.Default;

        Assert.Equal(created.AddHours(2), sla.ResponseDue(created));
        Assert.Equal(created.AddHours(4), sla.ResolveDue(created, IssuePriority.Critical));
        Assert.Equal(created.AddHours(24), sla.ResolveDue(created, IssuePriority.High));
        Assert.Equal(created.AddHours(72), sla.ResolveDue(created, IssuePriority.Medium));
        Assert.Equal(created.AddHours(168), sla.ResolveDue(created, IssuePriority.Low));
    }

    [Theory]
    [InlineData(IssueImpact.Blocking, IssuePriority.High)]
    [InlineData(IssueImpact.Degraded, IssuePriority.Medium)]
    [InlineData(IssueImpact.Minor, IssuePriority.Low)]
    public void The_starting_priority_follows_the_reporters_impact_answer(IssueImpact impact, IssuePriority priority) =>
        Assert.Equal(priority, IssueSlaPolicy.InitialPriority(impact));

    [Fact]
    public void The_display_number_uses_the_buddhist_year_and_pads_the_sequence()
    {
        Assert.Equal("ISS-2569-0001", KPI.Domain.Entities.Issue.FormatNumber(2026, 1));
        Assert.Equal("ISS-2570-0123", KPI.Domain.Entities.Issue.FormatNumber(2027, 123));
    }
}
