using KPI.Application.Notifications;
using KPI.Domain.Enums;
using KPI.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class DelayDetectionJobTests
{
    private static (DelayDetectionJob Job, RecordingEmail Email) NewJob(TestWorld w) =>
        (new DelayDetectionJob(w.Db, w.Events, NullLogger<DelayDetectionJob>.Instance), w.Email);

    [Fact]
    public async Task Overdue_running_kpis_become_Delayed_without_anyone_touching_them()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, progress: 30,
            start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-3));
        var (job, _) = NewJob(w);

        await job.RunAsync();

        w.Clear();
        Assert.Equal(KpiStatus.Delayed, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
        var audit = Assert.Single(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "AutoStatusChange");
        Assert.Equal("Active", audit.OldValues);
        Assert.Equal("Delayed", audit.NewValues);
        Assert.Contains(w.Notifier.Notifications,
            n => n.Audience.UserIds.Contains(w.ManagerA.Id) && n.Audience.ManagersOfDepartment == w.DeptA.Id);
    }

    [Fact]
    public async Task A_kpi_that_caught_up_returns_from_AtRisk_to_Active()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.AtRisk, w.ManagerA, progress: 55,
            start: DateTime.UtcNow.AddDays(-60), due: DateTime.UtcNow.AddDays(40));
        var (job, email) = NewJob(w);

        await job.RunAsync();

        w.Clear();
        Assert.Equal(KpiStatus.Active, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
        Assert.Empty(email.Sent); // recovering is good news, not an alert
    }

    [Fact]
    public async Task Only_running_kpis_are_evaluated_and_unchanged_ones_are_left_alone()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA, start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-3));
        var closed = await w.AddKpiAsync(KpiStatus.Closed, w.ManagerA, start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-3));
        var steady = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, progress: 10);
        var versionBefore = steady.Version;
        var (job, _) = NewJob(w);

        await job.RunAsync();

        w.Clear();
        Assert.Equal(KpiStatus.Draft, (await w.Db.Kpis.FindAsync(draft.Id))!.Status);
        Assert.Equal(KpiStatus.Closed, (await w.Db.Kpis.FindAsync(closed.Id))!.Status);
        Assert.Equal(versionBefore, (await w.Db.Kpis.FindAsync(steady.Id))!.Version);
        Assert.Empty(await w.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Running_the_job_twice_does_not_repeat_notifications()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, progress: 30,
            start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-3));
        var (job, _) = NewJob(w);

        await job.RunAsync();
        var afterFirst = w.Notifier.Notifications.Count;
        await job.RunAsync();

        Assert.Equal(afterFirst, w.Notifier.Notifications.Count);
    }

    [Fact]
    public async Task A_mail_failure_cannot_undo_the_status_change()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, progress: 5,
            start: DateTime.UtcNow.AddDays(-60), due: DateTime.UtcNow.AddDays(40));
        var (job, email) = NewJob(w);
        email.Throw = true;

        await job.RunAsync(); // must not throw

        w.Clear();
        Assert.Equal(KpiStatus.AtRisk, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
    }

    [Fact]
    public async Task Becoming_AtRisk_emails_the_owner_once()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, progress: 5,
            start: DateTime.UtcNow.AddDays(-60), due: DateTime.UtcNow.AddDays(40));
        var (job, email) = NewJob(w);

        await job.RunAsync();
        await job.RunAsync(); // next day: nothing changed, nothing sent

        Assert.Equal([w.ManagerA.Email], email.To(KpiEmailKind.AtRisk));
    }

    [Fact]
    public async Task Becoming_Delayed_emails_the_owner_and_department_managers_without_duplicates()
    {
        var w = await TestWorld.CreateAsync();
        // Owned by a team leader, so the owner and the department manager are different people.
        await w.AddKpiAsync(KpiStatus.Active, w.TeamLeaderA, progress: 30,
            start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-3));
        var (job, email) = NewJob(w);

        await job.RunAsync();

        Assert.Equal(
            new[] { w.ManagerA.Email, w.TeamLeaderA.Email }.OrderBy(e => e),
            email.To(KpiEmailKind.Delayed).OrderBy(e => e));
        Assert.DoesNotContain(email.Sent, m => m.ToEmail == w.ManagerB.Email); // other department
    }
}
