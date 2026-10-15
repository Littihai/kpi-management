using KPI.Application.Kpis;
using KPI.Domain.Entities;
using KPI.Domain.Enums;
using KPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

public class KpiServiceTests
{
    private static CreateKpiRequest NewRequest(Guid departmentId, string name = "Reduce downtime") =>
        new(name, "desc", 2026, 100, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), departmentId);

    // ───────────── Creating ─────────────

    [Fact]
    public async Task Manager_creates_a_Draft_in_their_own_department()
    {
        var w = await TestWorld.CreateAsync();

        var dto = await w.Service.CreateAsync(NewRequest(w.DeptA.Id), w.ManagerA.Id);

        Assert.Equal(KpiStatus.Draft, dto.Status);
        Assert.Equal(w.ManagerA.Id, dto.OwnerId);
        Assert.Equal(w.DeptA.Name, dto.DepartmentName);
        Assert.Contains(KpiActionNames.Submit, dto.AllowedActions);
        Assert.Contains(KpiActionNames.Edit, dto.AllowedActions);
        Assert.DoesNotContain(KpiActionNames.Delete, dto.AllowedActions); // SuperAdmin only
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "Create");
    }

    [Fact]
    public async Task Manager_cannot_create_in_another_department()
    {
        var w = await TestWorld.CreateAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.CreateAsync(NewRequest(w.DeptB.Id), w.ManagerA.Id));
    }

    [Fact]
    public async Task SuperAdmin_can_create_in_any_department()
    {
        var w = await TestWorld.CreateAsync();
        var dto = await w.Service.CreateAsync(NewRequest(w.DeptB.Id), w.SuperAdmin.Id);
        Assert.Equal(w.DeptB.Id, dto.DepartmentId);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Viewer")]
    [InlineData("TeamLeader")]
    [InlineData("Director")]
    public async Task Roles_without_kpi_create_are_refused(string who)
    {
        var w = await TestWorld.CreateAsync();
        var user = who switch
        {
            "Staff" => w.StaffA,
            "Viewer" => w.ViewerA,
            "TeamLeader" => w.TeamLeaderA,
            _ => w.Director,
        };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.CreateAsync(NewRequest(w.DeptA.Id), user.Id));
    }

    [Fact]
    public async Task Inactive_users_are_refused_even_with_the_right_role()
    {
        var w = await TestWorld.CreateAsync();
        var gone = await w.AddUserAsync("gone", "Manager", w.DeptA.Id, active: false);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.CreateAsync(NewRequest(w.DeptA.Id), gone.Id));
    }

    [Theory]
    [InlineData("", 100, 2026)]
    [InlineData("   ", 100, 2026)]
    [InlineData("ok", 0, 2026)]
    [InlineData("ok", 101, 2026)]
    [InlineData("ok", -5, 2026)]
    [InlineData("ok", 100, 1900)]
    public async Task Invalid_input_is_rejected(string name, decimal target, int year)
    {
        var w = await TestWorld.CreateAsync();
        var request = new CreateKpiRequest(name, "", year, target,
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), w.DeptA.Id);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Service.CreateAsync(request, w.ManagerA.Id));
    }

    [Fact]
    public async Task Due_date_must_be_after_start_date()
    {
        var w = await TestWorld.CreateAsync();
        var request = new CreateKpiRequest("ok", "", 2026, 100,
            new DateTime(2026, 6, 1), new DateTime(2026, 6, 1), w.DeptA.Id);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Service.CreateAsync(request, w.ManagerA.Id));
    }

    [Fact]
    public async Task Unknown_department_is_rejected()
    {
        var w = await TestWorld.CreateAsync();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Service.CreateAsync(NewRequest(Guid.NewGuid()), w.SuperAdmin.Id));
    }

    // ───────────── Visibility / department isolation ─────────────

    [Fact]
    public async Task Lists_are_limited_to_the_callers_department()
    {
        var w = await TestWorld.CreateAsync();
        var inA = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, w.DeptA);
        var inB = await w.AddKpiAsync(KpiStatus.Active, w.ManagerB, w.DeptB);

        Assert.Equal([inA.Id], (await w.Service.GetAllAsync(w.ManagerA.Id)).Select(k => k.Id));
        Assert.Equal([inA.Id], (await w.Service.GetAllAsync(w.ViewerA.Id)).Select(k => k.Id));
        Assert.Equal([inB.Id], (await w.Service.GetAllAsync(w.ManagerB.Id)).Select(k => k.Id));
        Assert.Equal(2, (await w.Service.GetAllAsync(w.Director.Id)).Count);
        Assert.Equal(2, (await w.Service.GetAllAsync(w.SuperAdmin.Id)).Count);
    }

    [Fact]
    public async Task A_user_without_a_department_sees_nothing()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, w.DeptA);
        Assert.Empty(await w.Service.GetAllAsync(w.NoDepartment.Id));
    }

    [Fact]
    public async Task Opening_another_departments_kpi_by_id_reports_not_found()
    {
        var w = await TestWorld.CreateAsync();
        var inB = await w.AddKpiAsync(KpiStatus.Active, w.ManagerB, w.DeptB);

        await Assert.ThrowsAsync<NotFoundException>(() => w.Service.GetByIdAsync(inB.Id, w.ManagerA.Id));
        Assert.Equal(inB.Id, (await w.Service.GetByIdAsync(inB.Id, w.Director.Id)).Id);
    }

    [Fact]
    public async Task Acting_on_another_departments_kpi_reports_not_found()
    {
        var w = await TestWorld.CreateAsync();
        var inB = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerB, w.DeptB);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Service.SubmitForApprovalAsync(inB.Id, w.ManagerA.Id));
    }

    // ───────────── Submit / approve / reject ─────────────

    [Fact]
    public async Task Draft_is_submitted_and_approvers_are_notified()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        var dto = await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Equal(KpiStatus.PendingApproval, dto.Status);
        Assert.NotNull(dto.SubmittedAt);
        Assert.Single(w.Notifier.Notifications, n => n.Audience.Approvers);
        Assert.Contains(w.Notifier.Events, e => e.Action == "submitted" && e.KpiId == kpi.Id);
    }

    [Fact]
    public async Task Only_a_draft_can_be_submitted()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id));
    }

    [Fact]
    public async Task Director_approves_and_the_owner_is_told()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);

        var dto = await w.Service.ApproveAsync(kpi.Id, w.Director.Id);

        Assert.Equal(KpiStatus.Active, dto.Status);
        Assert.NotNull(dto.ApprovedAt);
        Assert.Equal(w.Director.Id, (await w.Db.Kpis.FindAsync(kpi.Id))!.ApprovedById);
        Assert.Contains(w.Notifier.Notifications, n => n.Audience.UserIds.Contains(w.ManagerA.Id));
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "Approve" && a.UserId == w.Director.Id);
    }

    [Fact]
    public async Task Manager_cannot_approve_even_their_own_departments_kpi()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.StaffA);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Service.ApproveAsync(kpi.Id, w.ManagerA.Id));
    }

    [Fact]
    public async Task Nobody_approves_their_own_kpi()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.SuperAdmin);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Service.ApproveAsync(kpi.Id, w.SuperAdmin.Id));
    }

    [Fact]
    public async Task Only_a_pending_kpi_can_be_approved()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Service.ApproveAsync(kpi.Id, w.Director.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejecting_requires_a_reason(string reason)
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Service.RejectAsync(kpi.Id, reason, w.Director.Id));
        Assert.Equal(KpiStatus.PendingApproval, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
    }

    [Fact]
    public async Task Reject_returns_the_kpi_to_draft_with_the_reason_and_tells_the_owner()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);

        var dto = await w.Service.RejectAsync(kpi.Id, "Target too low", w.Director.Id);

        Assert.Equal(KpiStatus.Draft, dto.Status);
        Assert.Equal("Target too low", dto.RejectReason);
        Assert.Null(dto.SubmittedAt);
        Assert.Contains(w.Notifier.Notifications,
            n => n.Notification.Level == "warning" && n.Audience.UserIds.Contains(w.ManagerA.Id));
    }

    [Fact]
    public async Task A_resubmitted_kpi_clears_the_old_rejection_reason()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);
        await w.Service.RejectAsync(kpi.Id, "Fix it", w.Director.Id);

        var dto = await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Null(dto.RejectReason);
    }

    // ───────────── Progress (the original workflow-bypass bug) ─────────────

    [Theory]
    [InlineData(KpiStatus.Draft)]
    [InlineData(KpiStatus.PendingApproval)]
    [InlineData(KpiStatus.Closed)]
    public async Task Progress_cannot_be_posted_outside_a_running_or_completed_kpi(KpiStatus status)
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(status, w.ManagerA);

        await Assert.ThrowsAsync<InvalidStateException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 50, ""), w.ManagerA.Id));

        w.Clear();
        Assert.Equal(status, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status); // not silently promoted to Active
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Progress_must_stay_between_zero_and_the_target(decimal progress)
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, progress, ""), w.ManagerA.Id));
    }

    [Fact]
    public async Task Progress_update_writes_a_log_an_audit_row_and_bumps_the_version_together()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);
        var versionBefore = kpi.Version;

        var dto = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 20, "good month"), w.ManagerA.Id);

        Assert.Equal(20, dto.Progress);
        var log = Assert.Single(await w.Db.KpiProgressLogs.ToListAsync());
        Assert.Equal("good month", log.Notes);
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "UpdateProgress");
        Assert.Equal(versionBefore + 1, (await w.Db.Kpis.FindAsync(kpi.Id))!.Version);
    }

    [Fact]
    public async Task A_behind_schedule_kpi_becomes_AtRisk_and_a_recovered_one_returns_to_Active()
    {
        var w = await TestWorld.CreateAsync();
        // 60 of 100 days elapsed → ~60 expected.
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA,
            start: DateTime.UtcNow.AddDays(-60), due: DateTime.UtcNow.AddDays(40));

        var behind = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 10, ""), w.ManagerA.Id);
        Assert.Equal(KpiStatus.AtRisk, behind.Status);
        Assert.Contains(w.Notifier.Notifications, n => n.Notification.Title.Contains("AtRisk"));

        // The reported bug: AtRisk KPIs must still accept progress and be able to recover.
        var recovered = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 58, ""), w.ManagerA.Id);
        Assert.Equal(KpiStatus.Active, recovered.Status);
    }

    [Fact]
    public async Task An_overdue_kpi_is_Delayed_and_still_accepts_progress()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Delayed, w.ManagerA,
            start: DateTime.UtcNow.AddDays(-100), due: DateTime.UtcNow.AddDays(-5), progress: 40);

        var dto = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 60, ""), w.ManagerA.Id);

        Assert.Equal(KpiStatus.Delayed, dto.Status);
        Assert.Equal(60, dto.Progress);
    }

    [Fact]
    public async Task Reaching_the_target_completes_and_lowering_it_reopens()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);

        var done = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 100, ""), w.ManagerA.Id);
        Assert.Equal(KpiStatus.Completed, done.Status);
        Assert.Contains(KpiActionNames.UpdateProgress, done.AllowedActions);

        // typo correction before it is closed
        var fixedUp = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 10, ""), w.ManagerA.Id);
        Assert.True(KPI.Domain.Kpis.KpiStateMachine.IsInProgress(fixedUp.Status));
    }

    [Fact]
    public async Task Staff_can_update_only_their_own_kpi()
    {
        var w = await TestWorld.CreateAsync();
        var theirs = await w.AddKpiAsync(KpiStatus.Active, w.StaffA);
        var other = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);

        var ok = await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(theirs.Id, 5, ""), w.StaffA.Id);
        Assert.Equal(5, ok.Progress);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(other.Id, 5, ""), w.StaffA.Id));
    }

    [Fact]
    public async Task Team_leader_can_update_progress_in_their_department_but_not_elsewhere()
    {
        var w = await TestWorld.CreateAsync();
        var inA = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA, w.DeptA);
        var inB = await w.AddKpiAsync(KpiStatus.Active, w.ManagerB, w.DeptB);

        await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(inA.Id, 5, ""), w.TeamLeaderA.Id);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(inB.Id, 5, ""), w.TeamLeaderA.Id));
    }

    [Fact]
    public async Task Viewer_and_Director_cannot_update_progress()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 5, ""), w.ViewerA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 5, ""), w.Director.Id));
    }

    // ───────────── Close / edit / delete ─────────────

    [Fact]
    public async Task Director_closes_a_completed_kpi_and_it_is_then_frozen()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Completed, w.ManagerA, progress: 100);

        var dto = await w.Service.CloseAsync(kpi.Id, w.Director.Id);

        Assert.Equal(KpiStatus.Closed, dto.Status);
        Assert.NotNull(dto.ClosedAt);
        Assert.Empty(dto.AllowedActions);
        await Assert.ThrowsAsync<InvalidStateException>(() =>
            w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 50, ""), w.ManagerA.Id));
    }

    [Fact]
    public async Task Only_a_completed_kpi_can_be_closed_and_managers_cannot_close()
    {
        var w = await TestWorld.CreateAsync();
        var active = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);
        var completed = await w.AddKpiAsync(KpiStatus.Completed, w.ManagerA, progress: 100);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Service.CloseAsync(active.Id, w.Director.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Service.CloseAsync(completed.Id, w.ManagerA.Id));
    }

    [Fact]
    public async Task Draft_can_be_edited_but_a_pending_one_is_locked()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);
        var pending = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);
        var update = new UpdateKpiRequest("Renamed", "d", 2026, 80, new DateTime(2026, 1, 1), new DateTime(2026, 11, 30), w.DeptA.Id);

        var dto = await w.Service.UpdateAsync(draft.Id, update, w.ManagerA.Id);
        Assert.Equal("Renamed", dto.Name);
        Assert.Equal(80, dto.Target);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Service.UpdateAsync(pending.Id, update, w.ManagerA.Id));
    }

    [Fact]
    public async Task Only_SuperAdmin_deletes_and_only_drafts()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);
        var active = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Service.DeleteAsync(draft.Id, w.ManagerA.Id));
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Service.DeleteAsync(active.Id, w.SuperAdmin.Id));

        await w.Service.DeleteAsync(draft.Id, w.SuperAdmin.Id);
        Assert.Null(await w.Db.Kpis.FindAsync(draft.Id));
    }

    // ───────────── Real-time resilience ─────────────

    [Fact]
    public async Task A_failing_real_time_push_never_fails_the_operation()
    {
        var w = await TestWorld.CreateAsync();
        w.Notifier.Throw = true;
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        var dto = await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Equal(KpiStatus.PendingApproval, dto.Status);
        w.Clear();
        Assert.Equal(KpiStatus.PendingApproval, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
    }

    // ───────────── Concurrency ─────────────

    [Fact]
    public async Task Two_approvers_cannot_both_win()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);

        // A second request (its own context, as in a real second HTTP request) loads the same row.
        await using var other = new KPI.Infrastructure.Data.AppDbContext(w.Options);
        var otherService = new KPI.Infrastructure.Services.KpiService(
            new KPI.Infrastructure.Repositories.KpiRepository(other),
            new KPI.Infrastructure.Repositories.UserRepository(other), other, w.Events);

        // The first request loads the KPI, then the second one approves it before the first saves.
        var loadedByFirst = await w.Db.Kpis.FirstAsync(k => k.Id == kpi.Id);
        await otherService.ApproveAsync(kpi.Id, w.Director.Id);

        loadedByFirst.Status = KpiStatus.Active;
        loadedByFirst.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.Db.SaveChangesAsync());
    }
}
