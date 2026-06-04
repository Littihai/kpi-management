namespace KPI.Application.Jobs;

public interface IDelayDetectionJob
{
    Task RunAsync();
}