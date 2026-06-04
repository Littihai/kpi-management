namespace KPI.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync();
}