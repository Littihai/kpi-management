namespace KPI.Application.Jobs;

/// <summary>Daily housekeeping: close resolved issues nobody answered, purge old request logs.</summary>
public interface IMaintenanceJob
{
    Task RunAsync();
}
