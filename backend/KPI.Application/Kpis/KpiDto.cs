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
    string DepartmentName,
    string OwnerName
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

public record UpdateKpiProgressRequest(
    Guid KpiId,
    decimal Progress,
    string Notes
);