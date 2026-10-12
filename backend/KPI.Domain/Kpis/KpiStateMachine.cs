using KPI.Domain.Enums;
using KPI.Domain.Exceptions;

namespace KPI.Domain.Kpis;

public enum KpiAction
{
    Edit,
    Delete,
    Submit,
    Approve,
    Reject,
    UpdateProgress,
    Close,
}

/// <summary>
/// Single source of truth for which KPI actions are valid in which status, and for the
/// automatic Active / AtRisk / Delayed / Completed evaluation.
/// </summary>
public static class KpiStateMachine
{
    /// <summary>How far (in target units) progress may trail the expected pace before a KPI is AtRisk.</summary>
    public const decimal AtRiskTolerance = 15m;

    public static bool IsInProgress(KpiStatus status) =>
        status is KpiStatus.Active or KpiStatus.AtRisk or KpiStatus.Delayed;

    public static bool CanApply(KpiStatus status, KpiAction action) => action switch
    {
        KpiAction.Edit or KpiAction.Delete or KpiAction.Submit => status == KpiStatus.Draft,
        KpiAction.Approve or KpiAction.Reject => status == KpiStatus.PendingApproval,
        KpiAction.UpdateProgress => IsInProgress(status) || status == KpiStatus.Completed,
        KpiAction.Close => status == KpiStatus.Completed,
        _ => false,
    };

    public static void EnsureCanApply(KpiStatus status, KpiAction action)
    {
        if (!CanApply(status, action))
            throw new InvalidStateException($"Action '{action}' is not allowed while the KPI is {status}.");
    }

    /// <summary>
    /// Status a running (or just-completed) KPI should have right now.
    /// Order matters: target reached, then overdue, then behind pace, otherwise on track.
    /// A due date counts as the end of that day.
    /// </summary>
    public static KpiStatus EvaluateHealth(
        decimal progress, decimal target, DateTime startDate, DateTime dueDate, DateTime now)
    {
        if (progress >= target) return KpiStatus.Completed;
        if (now.Date > dueDate.Date) return KpiStatus.Delayed;

        var totalDays = (dueDate - startDate).TotalDays;
        var ratio = totalDays > 0
            ? Math.Clamp((now - startDate).TotalDays / totalDays, 0d, 1d)
            : 1d;
        var expected = (decimal)ratio * target;

        return progress < expected - AtRiskTolerance ? KpiStatus.AtRisk : KpiStatus.Active;
    }

    public static string TrafficLight(KpiStatus status) => status switch
    {
        KpiStatus.Active or KpiStatus.Completed => "green",
        KpiStatus.AtRisk => "yellow",
        KpiStatus.Delayed => "red",
        _ => "gray",
    };
}
