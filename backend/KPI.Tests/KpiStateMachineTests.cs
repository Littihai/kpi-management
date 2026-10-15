using KPI.Domain.Enums;
using KPI.Domain.Exceptions;
using KPI.Domain.Kpis;

namespace KPI.Tests;

public class KpiStateMachineTests
{
    // The agreed transition table: action → the only statuses it may start from.
    private static readonly Dictionary<KpiAction, KpiStatus[]> Allowed = new()
    {
        [KpiAction.Edit] = [KpiStatus.Draft],
        [KpiAction.Delete] = [KpiStatus.Draft],
        [KpiAction.Submit] = [KpiStatus.Draft],
        [KpiAction.Approve] = [KpiStatus.PendingApproval],
        [KpiAction.Reject] = [KpiStatus.PendingApproval],
        [KpiAction.UpdateProgress] = [KpiStatus.Active, KpiStatus.AtRisk, KpiStatus.Delayed, KpiStatus.Completed],
        [KpiAction.Close] = [KpiStatus.Completed],
    };

    public static IEnumerable<object[]> EveryStatusAndAction() =>
        from status in Enum.GetValues<KpiStatus>()
        from action in Enum.GetValues<KpiAction>()
        select new object[] { status, action };

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void CanApply_matches_the_transition_table(KpiStatus status, KpiAction action)
    {
        var expected = Allowed[action].Contains(status);
        Assert.Equal(expected, KpiStateMachine.CanApply(status, action));
    }

    [Fact]
    public void Closed_and_the_unused_Approved_status_allow_nothing()
    {
        foreach (var action in Enum.GetValues<KpiAction>())
        {
            Assert.False(KpiStateMachine.CanApply(KpiStatus.Closed, action));
            Assert.False(KpiStateMachine.CanApply(KpiStatus.Approved, action));
        }
    }

    [Fact]
    public void EnsureCanApply_throws_InvalidStateException_for_a_bad_transition()
    {
        Assert.Throws<InvalidStateException>(() =>
            KpiStateMachine.EnsureCanApply(KpiStatus.Draft, KpiAction.UpdateProgress));
        KpiStateMachine.EnsureCanApply(KpiStatus.Draft, KpiAction.Submit); // no throw
    }

    // ── EvaluateHealth ──

    private static readonly DateTime Start = new(2026, 1, 1);
    private static readonly DateTime Due = new(2026, 12, 31);

    [Fact]
    public void Reaching_the_target_is_Completed_even_when_overdue()
    {
        var now = Due.AddDays(30);
        Assert.Equal(KpiStatus.Completed, KpiStateMachine.EvaluateHealth(100, 100, Start, Due, now));
    }

    [Fact]
    public void Past_the_due_date_without_reaching_target_is_Delayed()
    {
        Assert.Equal(KpiStatus.Delayed,
            KpiStateMachine.EvaluateHealth(50, 100, Start, Due, Due.AddDays(1)));
    }

    [Fact]
    public void The_due_date_itself_still_counts_as_on_time()
    {
        var endOfDueDay = Due.AddHours(23);
        Assert.NotEqual(KpiStatus.Delayed,
            KpiStateMachine.EvaluateHealth(50, 100, Start, Due, endOfDueDay));
    }

    [Fact]
    public void Trailing_the_expected_pace_by_more_than_the_tolerance_is_AtRisk()
    {
        var halfway = Start.AddDays((Due - Start).TotalDays / 2); // expected ≈ 50
        Assert.Equal(KpiStatus.AtRisk, KpiStateMachine.EvaluateHealth(30, 100, Start, Due, halfway));
    }

    [Fact]
    public void Trailing_by_exactly_the_tolerance_is_still_Active()
    {
        var halfway = Start.AddDays((Due - Start).TotalDays / 2);
        var expected = 50m;
        var progress = expected - KpiStateMachine.AtRiskTolerance;
        Assert.Equal(KpiStatus.Active, KpiStateMachine.EvaluateHealth(progress, 100, Start, Due, halfway));
    }

    [Fact]
    public void On_pace_is_Active()
    {
        var halfway = Start.AddDays((Due - Start).TotalDays / 2);
        Assert.Equal(KpiStatus.Active, KpiStateMachine.EvaluateHealth(50, 100, Start, Due, halfway));
    }

    [Fact]
    public void Before_the_start_date_nothing_is_expected_yet()
    {
        Assert.Equal(KpiStatus.Active,
            KpiStateMachine.EvaluateHealth(0, 100, Start, Due, Start.AddDays(-20)));
    }

    [Theory]
    [InlineData(KpiStatus.Active, "green")]
    [InlineData(KpiStatus.Completed, "green")]
    [InlineData(KpiStatus.AtRisk, "yellow")]
    [InlineData(KpiStatus.Delayed, "red")]
    [InlineData(KpiStatus.Draft, "gray")]
    [InlineData(KpiStatus.PendingApproval, "gray")]
    [InlineData(KpiStatus.Closed, "gray")]
    public void Traffic_light_follows_status(KpiStatus status, string color) =>
        Assert.Equal(color, KpiStateMachine.TrafficLight(status));
}
