using KPI.Application.Issues;
using KPI.Application.Jobs;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Jobs;

public class MaintenanceJob : IMaintenanceJob
{
    private readonly IIssueService _issues;
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<MaintenanceJob> _logger;
    private readonly int _requestLogDays;

    public MaintenanceJob(
        IIssueService issues,
        AppDbContext db,
        TimeProvider time,
        IConfiguration config,
        ILogger<MaintenanceJob> logger)
    {
        _issues = issues;
        _db = db;
        _time = time;
        _logger = logger;
        _requestLogDays = int.TryParse(config["Audit:RequestLogRetentionDays"], out var d) ? Math.Max(7, d) : 90;
    }

    public async Task RunAsync()
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var closed = await _issues.AutoCloseResolvedAsync(now);

        // Request logs grow with traffic; business audit rows are kept.
        var cutoff = now.AddDays(-_requestLogDays);
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

        _logger.LogInformation("Maintenance: {Closed} resolved issue(s) auto-closed, {Purged} old request log(s) removed",
            closed, purged);
    }
}
