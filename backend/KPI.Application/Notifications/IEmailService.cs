namespace KPI.Application.Notifications;

public interface IEmailService
{
    Task SendKpiApprovedAsync(string toEmail, string fullName, string kpiName);
    Task SendKpiDelayedAsync(string toEmail, string fullName, string kpiName, int daysDelayed);
    Task SendTaskAssignedAsync(string toEmail, string fullName, string taskName);
    Task SendKpiCreatedAsync(string toEmail, string managerName, string kpiName, string ownerName);
Task SendKpiSubmittedAsync(string toEmail, string directorName, string kpiName, string ownerName);
}