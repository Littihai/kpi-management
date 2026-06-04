namespace KPI.Application.Dashboard;

public record DashboardDto(
    int TotalKpis,
    int ActiveKpis,
    int DelayedKpis,
    int OnTrackKpis,
    double OverallProgress,
    List<DepartmentRankingDto> DepartmentRankings,
    List<KpiSummaryDto> RecentKpis,
    List<MonthlyTrendDto> MonthlyTrend
);

public record DepartmentRankingDto(
    string DepartmentName,
    double AverageProgress,
    int TotalKpis,
    string TrafficLight
);

public record KpiSummaryDto(
    Guid Id,
    string Name,
    double Progress,
    string StatusLabel,
    string TrafficLight,
    string DepartmentName
);

public record MonthlyTrendDto(
    string Month,
    double AverageProgress
);