using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Issues;
using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KPI.Tests;

/// <summary>HD-1.3: resume, on hold, the SLA deadline after a priority change, and who a comment counts as.</summary>
public class IssueLifecycleTests
{
    private static CreateIssueRequest Report(IssueImpact impact = IssueImpact.Degraded) =>
        new("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง", IssueCategory.Bug, IssueModule.Application, impact);

    private static async Task<IssueDetailDto> ReportAsync(TestWorld w, User by, IssueImpact impact = IssueImpact.Degraded) =>
        await w.Issues.CreateAsync(Report(impact), [], by.Id, "10.1.2.3");

    /// <summary>A report by StaffA that IT has taken and started.</summary>
    private static async Task<IssueDetailDto> InProgressAsync(TestWorld w, IssueImpact impact = IssueImpact.Degraded)
    {
        var issue = await ReportAsync(w, w.StaffA, impact);
        await w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.ItA.Id);
        return await w.Issues.StartAsync(issue.Id, w.ItA.Id);
    }

    // ───────────── state machine ─────────────

    [Theory]
    [InlineData(IssueStatus.WaitingForReporter)]
    [InlineData(IssueStatus.OnHold)]
    public void Resume_continues_the_work_and_does_not_run_the_clock_while_paused(IssueStatus paused)
    {
        Assert.True(IssueStateMachine.CanApply(paused, IssueAction.Resume));
        Assert.Equal(IssueStatus.InProgress, IssueStateMachine.Next(paused, IssueAction.Resume));
        Assert.False(IssueStateMachine.ClockRuns(paused));
        Assert.True(IssueStateMachine.IsOpen(paused));
    }

    [Fact]
    public void An_issue_on_hold_is_open_but_never_overdue()
    {
        var now = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
        Assert.False(IssueStateMachine.IsResolveOverdue(IssueStatus.OnHold, now.AddDays(-30), now));
        Assert.False(IssueStateMachine.IsResponseOverdue(IssueStatus.OnHold, null, now.AddDays(-30), now));
    }

    [Fact]
    public void The_new_status_number_is_fixed_for_good()
    {
        // stored as an int; changing it would re-label every issue already on hold
        Assert.Equal(8, (int)IssueStatus.OnHold);
    }

    // ───────────── resume ─────────────

    [Fact]
    public async Task Staff_can_resume_a_ticket_that_waits_for_a_reporter_who_cannot_answer()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        var waiting = await w.Issues.RequestInfoAsync(issue.Id, "ช่วยบอกเลขเครื่องหน่อย", w.ItA.Id);
        var dueWhileWaiting = waiting.ResolveDueAt;

        w.Clock.Advance(TimeSpan.FromHours(10));
        var resumed = await w.Issues.ResumeAsync(issue.Id, "ผู้แจ้งไม่ได้ตอบ แต่ตรวจเจอจากบันทึกเครื่องแล้ว", w.ItA.Id);

        Assert.Equal(IssueStatus.InProgress, resumed.Status);
        Assert.Null(resumed.WaitingSince);
        Assert.Equal(dueWhileWaiting.AddHours(10), resumed.ResolveDueAt);   // pushed back by the time spent waiting
        Assert.Contains(resumed.Comments, c => c.IsStaff && !c.IsInternal && c.Body.Contains("ตรวจเจอจากบันทึกเครื่อง"));
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "Resume" && a.EntityName == "Issue");
        Assert.Contains(IssueActionNames.Resume, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).AllowedActions.Concat(waiting.AllowedActions));
    }

    [Fact]
    public async Task Resume_needs_a_note_a_member_of_the_queue_and_a_paused_issue()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.ResumeAsync(issue.Id, "x", w.ItA.Id)); // not paused

        await w.Issues.RequestInfoAsync(issue.Id, "ขอข้อมูลเพิ่ม", w.ItA.Id);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.ResumeAsync(issue.Id, "   ", w.ItA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.ResumeAsync(issue.Id, "ขอทำต่อ", w.StaffA.Id));
        Assert.Equal(IssueStatus.WaitingForReporter, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).Status);
    }

    [Fact]
    public async Task Resume_is_only_offered_while_the_issue_is_paused()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        Assert.DoesNotContain(IssueActionNames.Resume, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).AllowedActions);

        var held = await w.Issues.HoldAsync(issue.Id, "รออะไหล่", w.ItA.Id);
        Assert.Contains(IssueActionNames.Resume, held.AllowedActions);
        Assert.DoesNotContain(IssueActionNames.Hold, held.AllowedActions);
        Assert.DoesNotContain(IssueActionNames.Resume, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions); // the reporter cannot
    }

    // ───────────── on hold ─────────────

    [Fact]
    public async Task Putting_an_issue_on_hold_pauses_the_clock_and_resuming_gives_the_time_back()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        var due = issue.ResolveDueAt;

        var held = await w.Issues.HoldAsync(issue.Id, "รออะไหล่จากผู้ขาย", w.ItA.Id);
        Assert.Equal(IssueStatus.OnHold, held.Status);
        Assert.Equal(due, held.ResolveDueAt);
        Assert.Contains(held.Comments, c => c.IsStaff && c.Body.Contains("รออะไหล่จากผู้ขาย"));

        w.Clock.Advance(TimeSpan.FromHours(200));                        // far past the original target
        var stillHeld = await w.Issues.GetAsync(issue.Id, w.ItA.Id);
        Assert.False(stillHeld.ResolveOverdue);                          // a pause is not a breach

        var resumed = await w.Issues.ResumeAsync(issue.Id, "อะไหล่มาถึงแล้ว", w.ItA.Id);
        Assert.Equal(IssueStatus.InProgress, resumed.Status);
        Assert.Equal(due.AddHours(200), resumed.ResolveDueAt);
        Assert.False(resumed.ResolveOverdue);
    }

    [Fact]
    public async Task An_issue_on_hold_still_counts_as_open_and_shows_in_the_open_list()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        await w.Issues.HoldAsync(issue.Id, "รองบประมาณ", w.ItA.Id);

        var open = await w.Issues.ListAsync(new IssueQuery(OpenOnly: true), w.ItA.Id);
        var overdue = await w.Issues.ListAsync(new IssueQuery(Overdue: true), w.ItA.Id);
        var summary = await w.Issues.SummaryAsync(w.ItA.Id);

        Assert.Contains(open.Items, i => i.Id == issue.Id);
        Assert.DoesNotContain(overdue.Items, i => i.Id == issue.Id);
        Assert.Equal(1, summary.Open);
    }

    [Fact]
    public async Task Hold_needs_a_reason_a_started_issue_and_a_member_of_the_queue()
    {
        var w = await TestWorld.CreateAsync();
        var fresh = await ReportAsync(w, w.StaffA);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.HoldAsync(fresh.Id, "รออะไหล่", w.ItA.Id)); // still New

        var started = await InProgressAsync(w);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.HoldAsync(started.Id, " ", w.ItA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.HoldAsync(started.Id, "รออะไหล่", w.StaffA.Id));
    }

    [Fact]
    public async Task An_issue_on_hold_can_still_be_reassigned_or_rejected_but_not_resolved()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        await w.Issues.HoldAsync(issue.Id, "รออะไหล่", w.ItA.Id);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.ResolveAsync(issue.Id,
            new ResolveIssueRequest("แก้ไขแล้วเรียบร้อย", IssueRootCause.Infrastructure), w.ItA.Id));
        Assert.Equal(IssueStatus.OnHold, (await w.Issues.AssignAsync(issue.Id, w.ItB.Id, w.ItA.Id)).Status);
        Assert.Equal(IssueStatus.Rejected, (await w.Issues.RejectAsync(issue.Id, "ยกเลิกคำขอ", null, w.ItA.Id)).Status);
    }

    // ───────────── SLA after a priority change ─────────────

    [Fact]
    public void The_shift_moves_the_deadline_by_the_difference_between_the_two_windows()
    {
        var sla = IssueSlaPolicy.Default;
        var due = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal(due.AddHours(-48), sla.ShiftResolveDue(due, IssuePriority.Medium, IssuePriority.High));
        Assert.Equal(due.AddHours(96), sla.ShiftResolveDue(due, IssuePriority.Medium, IssuePriority.Low));
        Assert.Equal(due, sla.ShiftResolveDue(due, IssuePriority.High, IssuePriority.High));
        // and back again lands where it started
        Assert.Equal(due, sla.ShiftResolveDue(sla.ShiftResolveDue(due, IssuePriority.Low, IssuePriority.Critical), IssuePriority.Critical, IssuePriority.Low));
    }

    [Fact]
    public void Pausing_pushes_the_deadline_back_but_never_forward()
    {
        var due = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
        var since = due.AddHours(-5);
        Assert.Equal(due.AddHours(3), IssueSlaPolicy.PushBackForPause(due, since, since.AddHours(3)));
        Assert.Equal(due, IssueSlaPolicy.PushBackForPause(due, since, since.AddHours(-1))); // a clock step backwards changes nothing
    }

    [Fact]
    public async Task Without_any_waiting_a_new_priority_gives_the_same_deadline_as_before()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);                       // Degraded -> Medium, 72 h
        w.Clock.Advance(TimeSpan.FromHours(1));

        var raised = await w.Issues.SetPriorityAsync(issue.Id, IssuePriority.High, w.ItA.Id);

        Assert.Equal(issue.CreatedAt.AddHours(24), raised.ResolveDueAt);
    }

    [Fact]
    public async Task Waiting_time_already_credited_survives_a_later_priority_change()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);                              // Medium: due created + 72 h
        var created = issue.CreatedAt;

        w.Clock.Advance(TimeSpan.FromHours(10));
        await w.Issues.RequestInfoAsync(issue.Id, "ช่วยบอกเลขเครื่องหน่อย", w.ItA.Id);
        w.Clock.Advance(TimeSpan.FromHours(20));
        var answered = await w.Issues.CommentAsync(issue.Id, "เลขเครื่อง 42 ค่ะ", false, w.StaffA.Id);
        Assert.Equal(created.AddHours(92), answered.ResolveDueAt);         // 72 h + 20 h waited

        w.Clock.Advance(TimeSpan.FromHours(10));                           // 40 h after it was reported
        var raised = await w.Issues.SetPriorityAsync(issue.Id, IssuePriority.High, w.ItA.Id);

        Assert.Equal(created.AddHours(44), raised.ResolveDueAt);           // not created + 24 h, which was already past
        Assert.False(raised.ResolveOverdue);
    }

    [Fact]
    public async Task Raising_the_priority_of_a_reopened_issue_keeps_its_fresh_target()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        await w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("เปลี่ยนตลับหมึกแล้ว", IssueRootCause.Other), w.ItA.Id);

        w.Clock.Advance(TimeSpan.FromHours(100));
        var reopened = await w.Issues.ReopenAsync(issue.Id, "ยังพิมพ์ไม่ออก", w.StaffA.Id);   // a fresh 72 h from now
        Assert.Equal(w.Clock.UtcNow.AddHours(72), reopened.ResolveDueAt);

        w.Clock.Advance(TimeSpan.FromHours(1));
        var raised = await w.Issues.SetPriorityAsync(issue.Id, IssuePriority.Critical, w.ItA.Id);

        Assert.Equal(reopened.ResolveDueAt.AddHours(4 - 72), raised.ResolveDueAt);
        Assert.False(raised.ResolveOverdue);                               // created + 4 h would have been 97 h ago
    }

    // ───────────── who a comment counts as ─────────────

    [Fact]
    public async Task A_managers_comment_on_someone_elses_issue_is_not_the_reporter_answering()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);                              // StaffA's issue, same department as ManagerA
        await w.Issues.RequestInfoAsync(issue.Id, "ช่วยบอกเลขเครื่องหน่อย", w.ItA.Id);
        var waitingSince = (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).WaitingSince;
        w.Email.IssueSent.Clear();
        w.Notifier.Notifications.Clear();

        var after = await w.Issues.CommentAsync(issue.Id, "ขอให้รีบดูให้หน่อยครับ", false, w.ManagerA.Id);

        Assert.Equal(IssueStatus.WaitingForReporter, after.Status);        // the manager did not answer for the reporter
        Assert.Equal(waitingSince, after.WaitingSince);                    // and the clock stays paused
        Assert.Contains(after.Comments, c => c.Body.Contains("รีบดูให้หน่อย") && !c.IsStaff);
        Assert.Empty(w.Email.To(IssueEmailKind.ReporterReplied));
        Assert.DoesNotContain(w.Notifier.Notifications, n => n.Notification.Title == "ผู้แจ้งตอบกลับ");
        var audits = await w.Db.AuditLogs.Where(a => a.EntityName == "Issue").Select(a => a.Action).ToListAsync();
        Assert.Contains("Comment", audits);
        Assert.DoesNotContain("ReporterReply", audits);
    }

    [Fact]
    public async Task The_real_reporter_answering_still_resumes_the_work_and_tells_the_assignee()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        await w.Issues.RequestInfoAsync(issue.Id, "ช่วยบอกเลขเครื่องหน่อย", w.ItA.Id);
        w.Email.IssueSent.Clear();

        var answered = await w.Issues.CommentAsync(issue.Id, "เลขเครื่อง 42 ค่ะ", false, w.StaffA.Id);

        Assert.Equal(IssueStatus.InProgress, answered.Status);
        Assert.Equal([w.ItA.Email], w.Email.To(IssueEmailKind.ReporterReplied));
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "ReporterReply");
    }

    [Fact]
    public async Task Internal_notes_are_announced_to_the_queue_only_and_ordinary_events_to_everyone()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await InProgressAsync(w);
        w.Notifier.IssueEvents.Clear();

        await w.Issues.CommentAsync(issue.Id, "ดูเหมือนสายแลนหลวม ระวังอย่าเพิ่งบอกผู้แจ้ง", true, w.ItA.Id);
        await w.Issues.CommentAsync(issue.Id, "กำลังตรวจสอบอยู่ครับ", false, w.ItA.Id);

        var note = Assert.Single(w.Notifier.IssueEvents, e => e.Action == "note");
        Assert.True(note.StaffOnly);
        Assert.False(Assert.Single(w.Notifier.IssueEvents, e => e.Action == "comment").StaffOnly);
    }
}

/// <summary>The same promise over a real hub: the reporter's connection never hears about an internal note.</summary>
public class InternalNoteHubTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public InternalNoteHubTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubConnection Connect(string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/kpi"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    [Fact]
    public async Task The_reporter_does_not_receive_the_event_of_an_internal_note_but_IT_does()
    {
        var reporter = await _factory.ClientAsync("staff-a");
        var form = new MultipartFormDataContent
        {
            { new StringContent("เรื่องที่มีโน้ตภายใน"), "Title" },
            { new StringContent("รายละเอียดที่ยาวพอสำหรับการทดสอบ"), "Description" },
            { new StringContent("Bug"), "Category" },
            { new StringContent("Login"), "Module" },
            { new StringContent("Degraded"), "Impact" },
        };
        var created = await reporter.PostAsync("/api/issues", form);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var reporterEvents = new List<string>();
        var itEvents = new List<string>();
        await using var reporterHub = Connect(await _factory.TokenAsync("staff-a"));
        await using var itHub = Connect(await _factory.TokenAsync("it-b"));
        reporterHub.On<JsonElement>("IssueChanged", e => { lock (reporterEvents) reporterEvents.Add(e.GetProperty("action").GetString()!); });
        itHub.On<JsonElement>("IssueChanged", e => { lock (itEvents) itEvents.Add(e.GetProperty("action").GetString()!); });
        await reporterHub.StartAsync();
        await itHub.StartAsync();

        var it = await _factory.ClientAsync("it-a");
        var note = await it.PostAsJsonAsync($"/api/issues/{id}/comments", new { body = "โน้ตภายในที่ผู้แจ้งต้องไม่เห็น", internalNote = true });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) { lock (itEvents) if (itEvents.Contains("note")) break; await Task.Delay(25); }
        await Task.Delay(400); // time for a wrong delivery to the reporter to show up

        lock (itEvents) Assert.Contains("note", itEvents);
        lock (reporterEvents) Assert.DoesNotContain("note", reporterEvents);
    }
}
