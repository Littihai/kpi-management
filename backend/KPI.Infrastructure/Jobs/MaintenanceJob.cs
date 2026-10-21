using KPI.Application.Issues;
using KPI.Application.Auth;
using KPI.Application.Jobs;
using KPI.Application.Settings;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Jobs;

public class MaintenanceJob : IMaintenanceJob
{
    private readonly IIssueService _issues;
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<MaintenanceJob> _logger;
    private readonly ISystemSettings _settings;
    private readonly IAuthLog _authLog;

    public MaintenanceJob(
        IIssueService issues,
        AppDbContext db,
        TimeProvider time,
        ISystemSettings settings,
        IAuthLog authLog,
        ILogger<MaintenanceJob> logger)
    {
        _settings = settings;
        _authLog = authLog;
        _issues = issues;
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var closed = await _issues.AutoCloseResolvedAsync(now);

        // Request logs grow with traffic; business audit rows are kept.
        var cutoff = now.AddDays(-await _settings.GetNumberAsync(SettingCatalog.Keys.RequestLogDays));
        int purged;
        if (_db.Database.IsRelational())
            purged = await _db.RequestLogs.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync();
        else
        {
            var old = await _db.RequestLogs.Where(r => r.CreatedAt < cutoff).ToListAsync();
            _db.RequestLogs.RemoveRange(old);
            await _db.SaveChangesAsync();
            purged = old.Count;
        }

        var authPurged = await _authLog.PurgeOlderThanAsync(now.AddDays(-await _settings.GetNumberAsync(SettingCatalog.Keys.AuthLogDays)));

        _logger.LogInformation("Maintenance: {Closed} resolved issue(s) auto-closed, {Purged} old request log(s) and {AuthPurged} old sign-in log(s) removed",
            closed, purged, authPurged);
    }
}
