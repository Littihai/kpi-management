using KPI.Domain.Enums;

namespace KPI.Application.Kpis;

public record KpiDto(
    Guid Id,
    string Name,
    string Description,
    int Year,
    decimal Target,
    decimal Progress,
    KpiStatus Status,
    string StatusLabel,
    string TrafficLight,
    DateTime StartDate,
    DateTime DueDate,
    Guid DepartmentId,
    string DepartmentName,
    Guid OwnerId,
    string OwnerName,
    string? RejectReason,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? ClosedAt,
    // Actions the *current caller* may perform right now (permission + scope + status).
    IReadOnlyList<string> AllowedActions
);

public record CreateKpiRequest(
    string Name,
    string Description,
    int Year,
    decimal Target,
    DateTime StartDate,
    DateTime DueDate,
    Guid DepartmentId
);

public record UpdateKpiRequest(
    string Name,
    string Description,
    int Year,
    decimal Target,
    DateTime StartDate,
    DateTime DueDate,
    Guid DepartmentId
);

public record RejectKpiRequest(string Reason);

public record UpdateKpiProgressRequest(
    Guid KpiId,
    decimal Progress,
    string Notes
);

/// <summary>Names returned in <see cref="KpiDto.AllowedActions"/>.</summary>
public static class KpiActionNames
{
    public const string Edit = "edit";
    public const string Delete = "delete";
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string UpdateProgress = "updateProgress";
    public const string Close = "close";
}
