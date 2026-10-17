using KPI.Application.Issues;
using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class IssueServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static CreateIssueRequest Report(
        string title = "กดอนุมัติ KPI แล้วไม่มีอะไรเกิดขึ้น",
        IssueImpact impact = IssueImpact.Blocking,
        IssueModule module = IssueModule.Kpi) =>
        new(title, "กดปุ่ม Approve แล้วสถานะยังเป็น Pending ทั้งที่ควรเป็น Active", IssueCategory.Bug, module, impact);

    private static IReadOnlyList<IssueUpload> NoFiles => [];

    private static async Task<IssueDetailDto> ReportAsync(TestWorld w, User by, CreateIssueRequest? request = null) =>
        await w.Issues.CreateAsync(request ?? Report(), NoFiles, by.Id, "10.1.2.3");

    private static Task<IssueDetailDto> ResolveAsync(TestWorld w, Guid id, User by) =>
        w.Issues.ResolveAsync(id, new ResolveIssueRequest("แก้ไขสิทธิ์ของปุ่มอนุมัติแล้ว", IssueRootCause.CodeBug), by.Id);

    // ───────────── Reporting ─────────────

    [Fact]
    public async Task A_report_gets_a_number_a_priority_and_a_two_hour_response_target()
    {
        var w = await TestWorld.CreateAsync();

        var issue = await ReportAsync(w, w.StaffA);

        Assert.Equal("ISS-2569-0001", issue.Number);
        Assert.Equal(IssueStatus.New, issue.Status);
        Assert.Equal(IssuePriority.High, issue.Priority);          // "blocking" starts as High
        Assert.Equal(w.StaffA.Id, issue.ReporterUserId);
        Assert.Equal(w.DeptA.Id, issue.DepartmentId);
        Assert.Equal(w.Clock.UtcNow.AddHours(2), issue.ResponseDueAt);
        Assert.Equal(w.Clock.UtcNow.AddHours(24), issue.ResolveDueAt);
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "Create" && a.EntityName == "Issue");
    }

    [Fact]
    public async Task Numbers_run_on_within_a_year_and_start_again_in_the_next()
    {
        var w = await TestWorld.CreateAsync();

        var first = await ReportAsync(w, w.StaffA);
        var second = await ReportAsync(w, w.ManagerA);
        w.Clock.Advance(TimeSpan.FromDays(120));      // into 2027
        var next = await ReportAsync(w, w.StaffA);

        Assert.Equal("ISS-2569-0001", first.Number);
        Assert.Equal("ISS-2569-0002", second.Number);
        Assert.Equal("ISS-2570-0001", next.Number);
    }

    [Fact]
    public async Task IT_is_told_about_a_new_report_by_toast_and_e_mail_but_the_reporter_is_not_e_mailed_about_their_own_action()
    {
        var w = await TestWorld.CreateAsync();

        var issue = await ReportAsync(w, w.StaffA);

        Assert.Contains(w.Notifier.Notifications, n => n.Audience.ItSupport && n.Notification.IssueId == issue.Id);
        Assert.Contains(w.Notifier.IssueEvents, e => e.Action == "created" && e.IssueId == issue.Id);
        Assert.Equivalent(new[] { w.ItA.Email, w.ItB.Email }, w.Email.To(IssueEmailKind.NewForIt).ToArray());
        Assert.Empty(w.Email.To(IssueEmailKind.Received));
    }

    [Fact]
    public async Task Without_an_active_IT_team_the_SuperAdmin_is_e_mailed()
    {
        var w = await TestWorld.CreateAsync();
        w.ItA.IsActive = false;
        w.ItB.IsActive = false;
        await w.Db.SaveChangesAsync();

        await ReportAsync(w, w.StaffA);

        Assert.Equal([w.SuperAdmin.Email], w.Email.To(IssueEmailKind.NewForIt));
    }

    [Theory]
    [InlineData("ab", "long enough description here")]
    [InlineData("ok title", "too short")]
    public async Task Titles_and_descriptions_must_be_meaningful(string title, string description)
    {
        var w = await TestWorld.CreateAsync();
        var request = new CreateIssueRequest(title, description, IssueCategory.Bug, IssueModule.Kpi, IssueImpact.Minor);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CreateAsync(request, NoFiles, w.StaffA.Id, null));
    }

    [Fact]
    public async Task Unknown_enum_values_are_rejected()
    {
        var w = await TestWorld.CreateAsync();
        var request = Report() with { Module = (IssueModule)99 };
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CreateAsync(request, NoFiles, w.StaffA.Id, null));
    }

    [Fact]
    public async Task Inactive_users_cannot_report()
    {
        var w = await TestWorld.CreateAsync();
        var gone = await w.AddUserAsync("gone", "Staff", w.DeptA.Id, active: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => ReportAsync(w, gone));
    }

    // ───────────── Attachments ─────────────

    [Fact]
    public async Task Images_are_accepted_and_identified_by_their_content()
    {
        var w = await TestWorld.CreateAsync();

        var issue = await w.Issues.CreateAsync(Report(),
            [new IssueUpload("..\\..\\screen shot.png", "application/octet-stream", Png)], w.StaffA.Id, null);

        var file = Assert.Single(issue.Attachments);
        Assert.Equal("image/png", file.ContentType);        // detected, not trusted from the client
        Assert.Equal("screen shot.png", file.FileName);     // path stripped
        Assert.Equal(Png.Length, file.SizeBytes);
    }

    [Fact]
    public async Task A_number_collision_is_retried_without_attaching_the_images_twice()
    {
        var w = await TestWorld.CreateAsync();
        var failFirstSave = new FailFirstIssueSave();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>(w.Options).AddInterceptors(failFirstSave).Options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Enabled"] = "false" }).Build();
        var service = new IssueService(db, new UserRepository(db),
            new IssueEventPublisher(w.Notifier, db, w.Email, config, NullLogger<IssueEventPublisher>.Instance),
            IssueSlaPolicy.Default, w.Clock);

        var issue = await service.CreateAsync(Report(), [new IssueUpload("a.png", "image/png", Png)], w.StaffA.Id, null);

        Assert.Equal(1, failFirstSave.Failures);           // the first save really did fail
        Assert.Equal("ISS-2569-0001", issue.Number);
        Assert.Single(issue.Attachments);
        Assert.Equal(1, await w.Db.IssueAttachments.CountAsync());
        Assert.Equal(1, await w.Db.AuditLogs.CountAsync(a => a.Action == "Create"));
    }

    /// <summary>Stands in for two reports racing for the same number: the first save of a new issue is refused.</summary>
    private sealed class FailFirstIssueSave : SaveChangesInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Failures == 0 && eventData.Context!.ChangeTracker.Entries<Issue>().Any(e => e.State == EntityState.Added))
            {
                Failures++;
                throw new DbUpdateException("duplicate key value violates unique constraint");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_renamed_program_is_not_an_image()
    {
        var w = await TestWorld.CreateAsync();
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreateAsync(Report(), [new IssueUpload("cute.png", "image/png", exe)], w.StaffA.Id, null));
    }

    [Fact]
    public async Task Files_over_five_megabytes_and_more_than_five_files_are_refused()
    {
        var w = await TestWorld.CreateAsync();
        var big = new byte[IssueService.MaxAttachmentBytes + 1];
        Png.CopyTo(big, 0);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreateAsync(Report(), [new IssueUpload("big.png", "image/png", big)], w.StaffA.Id, null));

        var six = Enumerable.Range(0, 6).Select(i => new IssueUpload($"{i}.png", "image/png", Png)).ToList();
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CreateAsync(Report(), six, w.StaffA.Id, null));
    }

    [Fact]
    public async Task Attachments_can_be_added_later_and_downloaded_only_by_people_who_can_see_the_issue()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        var updated = await w.Issues.AddAttachmentsAsync(issue.Id, [new IssueUpload("a.png", "image/png", Png)], w.StaffA.Id);
        var attachment = Assert.Single(updated.Attachments);

        var content = await w.Issues.GetAttachmentAsync(issue.Id, attachment.Id, w.ItA.Id);
        Assert.Equal(Png, content.Data);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.GetAttachmentAsync(issue.Id, attachment.Id, w.StaffB.Id));
    }

    // ───────────── Who sees what ─────────────

    [Fact]
    public async Task People_see_their_own_managers_see_their_department_and_IT_sees_everything()
    {
        var w = await TestWorld.CreateAsync();
        var mine = await ReportAsync(w, w.StaffA);
        var colleague = await ReportAsync(w, w.TeamLeaderA);                 // same department
        var other = await ReportAsync(w, w.StaffB);                          // other department

        async Task<List<Guid>> Visible(User who) =>
            (await w.Issues.ListAsync(new IssueQuery(PageSize: 100), who.Id)).Items.Select(i => i.Id).ToList();

        Assert.Equal([mine.Id], await Visible(w.StaffA));
        Assert.Equivalent(new[] { mine.Id, colleague.Id }, await Visible(w.ManagerA));
        Assert.Equivalent(new[] { other.Id }, await Visible(w.ManagerB));
        foreach (var all in new[] { w.ItA, w.Director, w.SuperAdmin })
            Assert.Equal(3, (await Visible(all)).Count);
    }

    [Fact]
    public async Task Someone_elses_issue_is_reported_as_not_found()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.GetAsync(issue.Id, w.StaffB.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.CommentAsync(issue.Id, "hi", false, w.StaffB.Id));
        Assert.Equal(issue.Id, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).Id);
    }

    // ───────────── Working the issue ─────────────

    [Fact]
    public async Task The_whole_journey_from_report_to_closed()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        var assigned = await w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.ItB.Id);
        Assert.Equal(IssueStatus.Assigned, assigned.Status);
        Assert.Equal(w.ItA.Id, assigned.AssigneeId);
        Assert.NotNull(assigned.FirstResponseAt);
        Assert.Equal([w.ItA.Email], w.Email.To(IssueEmailKind.Assigned));

        var started = await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        Assert.Equal(IssueStatus.InProgress, started.Status);

        var resolved = await ResolveAsync(w, issue.Id, w.ItA);
        Assert.Equal(IssueStatus.Resolved, resolved.Status);
        Assert.Equal(IssueRootCause.CodeBug, resolved.RootCause);
        Assert.Equal("แก้ไขสิทธิ์ของปุ่มอนุมัติแล้ว", resolved.Resolution);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Contains(w.Notifier.Notifications, n => n.Notification.Title == "แก้ไขปัญหาแล้ว" && n.Audience.UserIds.Contains(w.StaffA.Id));
        Assert.Equal([w.StaffA.Email], w.Email.To(IssueEmailKind.Resolved));

        var closed = await w.Issues.ConfirmAsync(issue.Id, w.StaffA.Id);
        Assert.Equal(IssueStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);

        var steps = closed.Timeline.Select(t => t.Action).ToList();
        Assert.Equal(["Create", "Assign", "Start", "Resolve", "Confirm"], steps);
        Assert.All(closed.Timeline.Skip(1), t => Assert.NotNull(t.ActorName));
    }

    [Fact]
    public async Task Starting_an_unassigned_issue_takes_it()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        var started = await w.Issues.StartAsync(issue.Id, w.ItB.Id);

        Assert.Equal(w.ItB.Id, started.AssigneeId);
        Assert.Equal(IssueStatus.InProgress, started.Status);
    }

    [Fact]
    public async Task Only_IT_can_work_an_issue()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.StaffA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.StartAsync(issue.Id, w.StaffA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.RejectAsync(issue.Id, "no", null, w.StaffA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.SetPriorityAsync(issue.Id, IssuePriority.Critical, w.StaffA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AssigneesAsync(w.StaffA.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.ManagerA.Id));
    }

    [Fact]
    public async Task The_assignee_must_be_active_IT()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.AssignAsync(issue.Id, w.StaffA.Id, w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.AssignAsync(issue.Id, Guid.NewGuid(), w.ItA.Id));
    }

    [Fact]
    public async Task Resolving_needs_an_explanation_and_a_cause()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("  ", IssueRootCause.CodeBug), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("fixed it properly", IssueRootCause.None), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("fixed it properly", IssueRootCause.CodeBug, TimeSpentMinutes: -5), w.ItA.Id));
    }

    [Fact]
    public async Task A_new_issue_cannot_be_resolved_before_anyone_picks_it_up()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<InvalidStateException>(() => ResolveAsync(w, issue.Id, w.ItA));
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_a_real_duplicate()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        var original = await ReportAsync(w, w.StaffA, Report("เรื่องเดียวกันที่แจ้งไว้ก่อนแล้ว"));

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.RejectAsync(issue.Id, " ", null, w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.RejectAsync(issue.Id, "ซ้ำ", Guid.NewGuid(), w.ItA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.RejectAsync(issue.Id, "ซ้ำ", issue.Id, w.ItA.Id));

        var rejected = await w.Issues.RejectAsync(issue.Id, "ซ้ำกับเรื่องก่อนหน้า", original.Id, w.ItA.Id);
        Assert.Equal(IssueStatus.Rejected, rejected.Status);
        Assert.Equal(original.Id, rejected.DuplicateOfId);
        Assert.Equal("ซ้ำกับเรื่องก่อนหน้า", rejected.RejectReason);
        Assert.Equal([w.StaffA.Email], w.Email.To(IssueEmailKind.Rejected));
    }

    // ───────────── Waiting on the reporter ─────────────

    [Fact]
    public async Task Asking_for_information_stops_the_clock_and_the_reporters_answer_restarts_it()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        var dueBefore = (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).ResolveDueAt;

        w.Clock.Advance(TimeSpan.FromHours(1));
        var waiting = await w.Issues.RequestInfoAsync(issue.Id, "เกิดกับ KPI ตัวไหน และกดตอนกี่โมง", w.ItA.Id);
        Assert.Equal(IssueStatus.WaitingForReporter, waiting.Status);
        Assert.NotNull(waiting.WaitingSince);
        Assert.Equal(dueBefore, waiting.ResolveDueAt);
        Assert.Equal([w.StaffA.Email], w.Email.To(IssueEmailKind.NeedsInfo));

        // a day passes before the reporter answers; the deadline must not run out meanwhile
        w.Clock.Advance(TimeSpan.FromHours(10));
        var overdueWhileWaiting = (await w.Issues.ListAsync(new IssueQuery(Overdue: true), w.ItA.Id)).Items;
        Assert.Empty(overdueWhileWaiting);

        var resumed = await w.Issues.CommentAsync(issue.Id, "KPI ชื่อ Reduce downtime ตอนบ่ายสอง", false, w.StaffA.Id);
        Assert.Equal(IssueStatus.InProgress, resumed.Status);
        Assert.Null(resumed.WaitingSince);
        Assert.Equal(dueBefore.AddHours(10), resumed.ResolveDueAt);   // pushed back by the time spent waiting
        Assert.Equal([w.ItA.Email], w.Email.To(IssueEmailKind.ReporterReplied));
    }

    [Fact]
    public async Task Asking_for_information_needs_a_question()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.RequestInfoAsync(issue.Id, " ", w.ItA.Id));
    }

    // ───────────── Comments ─────────────

    [Fact]
    public async Task Internal_notes_are_for_IT_only_and_never_reach_the_reporter()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.CommentAsync(issue.Id, "secret", true, w.StaffA.Id));

        await w.Issues.CommentAsync(issue.Id, "ดูเหมือนบั๊กเก่า อย่าบอกผู้ใช้ก่อน", true, w.ItA.Id);
        await w.Issues.CommentAsync(issue.Id, "รับเรื่องแล้วครับ กำลังตรวจสอบ", false, w.ItA.Id);

        var forReporter = await w.Issues.GetAsync(issue.Id, w.StaffA.Id);
        Assert.Single(forReporter.Comments);
        Assert.DoesNotContain(forReporter.Timeline, t => t.Action == "InternalNote");

        var forIt = await w.Issues.GetAsync(issue.Id, w.ItB.Id);
        Assert.Equal(2, forIt.Comments.Count);
        Assert.Contains(forIt.Comments, c => c.IsInternal);
        Assert.Contains(forIt.Timeline, t => t.Action == "InternalNote");
        Assert.DoesNotContain(w.Email.To(IssueEmailKind.StaffReplied), e => e != w.StaffA.Email);
    }

    [Fact]
    public async Task A_public_reply_from_IT_counts_as_the_first_response_and_tells_the_reporter()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        Assert.Null(issue.FirstResponseAt);

        var replied = await w.Issues.CommentAsync(issue.Id, "รับเรื่องแล้วครับ", false, w.ItA.Id);

        Assert.Equal(w.Clock.UtcNow, replied.FirstResponseAt);
        Assert.Equal([w.StaffA.Email], w.Email.To(IssueEmailKind.StaffReplied));
        Assert.Contains(replied.Comments, c => c.IsStaff && c.AuthorName.Contains("it-a"));
    }

    [Fact]
    public async Task Empty_and_oversized_comments_are_refused()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CommentAsync(issue.Id, "  ", false, w.StaffA.Id));
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CommentAsync(issue.Id, new string('x', 4001), false, w.StaffA.Id));
    }

    // ───────────── Reopening ─────────────

    [Fact]
    public async Task The_reporter_can_say_it_is_not_fixed_and_the_issue_gets_a_fresh_target()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await ResolveAsync(w, issue.Id, w.ItA);
        w.Clock.Advance(TimeSpan.FromHours(30));

        var reopened = await w.Issues.ReopenAsync(issue.Id, "ยังกดไม่ได้เหมือนเดิม", w.StaffA.Id);

        Assert.Equal(IssueStatus.Reopened, reopened.Status);
        Assert.Equal(1, reopened.ReopenCount);
        Assert.Null(reopened.ResolvedAt);
        Assert.Equal(w.Clock.UtcNow.AddHours(24), reopened.ResolveDueAt);
        Assert.Contains(reopened.Comments, c => c.Body == "ยังกดไม่ได้เหมือนเดิม");
        Assert.Equal([w.ItA.Email], w.Email.To(IssueEmailKind.Reopened));

        await ResolveAsync(w, issue.Id, w.ItA); // can be fixed again
    }

    [Fact]
    public async Task A_reporter_may_reopen_a_closed_issue_for_a_week_then_only_IT_can()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await ResolveAsync(w, issue.Id, w.ItA);
        await w.Issues.ConfirmAsync(issue.Id, w.StaffA.Id);

        w.Clock.Advance(TimeSpan.FromDays(8));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.ReopenAsync(issue.Id, "กลับมาเป็นอีก", w.StaffA.Id));

        var byIt = await w.Issues.ReopenAsync(issue.Id, "ผู้ใช้โทรมาแจ้งว่ากลับมาเป็นอีก", w.ItA.Id);
        Assert.Equal(IssueStatus.Reopened, byIt.Status);
    }

    [Fact]
    public async Task Reopening_needs_a_reason_and_only_applies_to_finished_issues()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.ReopenAsync(issue.Id, "x", w.ItA.Id));
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await ResolveAsync(w, issue.Id, w.ItA);
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.ReopenAsync(issue.Id, " ", w.StaffA.Id));
    }

    [Fact]
    public async Task Only_the_reporter_or_IT_can_confirm_the_fix()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await ResolveAsync(w, issue.Id, w.ItA);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.ConfirmAsync(issue.Id, w.ManagerA.Id)); // can see it, but is not the reporter
        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.ConfirmAsync(issue.Id, w.StaffB.Id));
    }

    // ───────────── Deadlines, priority, upkeep ─────────────

    [Fact]
    public async Task An_unanswered_report_becomes_overdue_after_two_hours_until_IT_responds()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        Assert.False((await w.Issues.GetAsync(issue.Id, w.ItA.Id)).ResponseOverdue);

        w.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        var late = await w.Issues.GetAsync(issue.Id, w.ItA.Id);
        Assert.True(late.ResponseOverdue);
        var flagged = (await w.Issues.ListAsync(new IssueQuery(Overdue: true), w.ItA.Id)).Items;
        Assert.Equal([issue.Id], flagged.Select(i => i.Id));
        Assert.Equal(1, (await w.Issues.SummaryAsync(w.ItA.Id)).Overdue);

        await w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.ItB.Id);
        Assert.False((await w.Issues.GetAsync(issue.Id, w.ItA.Id)).ResponseOverdue);
    }

    [Fact]
    public async Task A_resolve_deadline_that_passes_is_flagged_but_finished_issues_never_are()
    {
        var w = await TestWorld.CreateAsync();
        var slow = await ReportAsync(w, w.StaffA);
        var done = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(slow.Id, w.ItA.Id);
        await w.Issues.StartAsync(done.Id, w.ItA.Id);
        await ResolveAsync(w, done.Id, w.ItA);

        w.Clock.Advance(TimeSpan.FromHours(25));

        Assert.True((await w.Issues.GetAsync(slow.Id, w.ItA.Id)).ResolveOverdue);
        Assert.False((await w.Issues.GetAsync(done.Id, w.ItA.Id)).ResolveOverdue);
    }

    [Fact]
    public async Task Changing_the_priority_moves_the_deadline_and_is_recorded()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA, Report(impact: IssueImpact.Minor)); // Low = 7 days
        Assert.Equal(w.Clock.UtcNow.AddHours(168), issue.ResolveDueAt);

        var critical = await w.Issues.SetPriorityAsync(issue.Id, IssuePriority.Critical, w.ItA.Id);

        Assert.Equal(IssuePriority.Critical, critical.Priority);
        Assert.Equal(w.Clock.UtcNow.AddHours(4), critical.ResolveDueAt);
        Assert.Contains(critical.Timeline, t => t.Action == "SetPriority" && t.From == "Low" && t.To == "Critical");
    }

    [Fact]
    public async Task Resolved_issues_the_reporter_ignores_are_closed_after_three_days()
    {
        var w = await TestWorld.CreateAsync();
        var ignored = await ReportAsync(w, w.StaffA);
        var recent = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(ignored.Id, w.ItA.Id);
        await ResolveAsync(w, ignored.Id, w.ItA);
        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.Issues.StartAsync(recent.Id, w.ItA.Id);
        await ResolveAsync(w, recent.Id, w.ItA);
        w.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1)); // ignored: 3d 1m, recent: 1d 1m

        var closed = await w.Issues.AutoCloseResolvedAsync(w.Clock.UtcNow);

        Assert.Equal(1, closed);
        Assert.Equal(IssueStatus.Closed, (await w.Issues.GetAsync(ignored.Id, w.ItA.Id)).Status);
        Assert.Equal(IssueStatus.Resolved, (await w.Issues.GetAsync(recent.Id, w.ItA.Id)).Status);
        Assert.Contains(await w.Db.AuditLogs.ToListAsync(), a => a.Action == "AutoClose" && a.UserEmail == "system");
    }

    [Fact]
    public async Task The_summary_counts_what_each_person_may_see()
    {
        var w = await TestWorld.CreateAsync();
        var a = await ReportAsync(w, w.StaffA);
        await ReportAsync(w, w.StaffB);
        await w.Issues.StartAsync(a.Id, w.ItA.Id);

        var it = await w.Issues.SummaryAsync(w.ItA.Id);
        Assert.Equal(2, it.Open);
        Assert.Equal(1, it.New);
        Assert.Equal(1, it.InProgress);

        var staff = await w.Issues.SummaryAsync(w.StaffA.Id);
        Assert.Equal(1, staff.Open);
        Assert.Equal(1, staff.Mine);
    }

    [Fact]
    public async Task The_queue_can_be_filtered_searched_and_sorted()
    {
        var w = await TestWorld.CreateAsync();
        var login = await ReportAsync(w, w.StaffA, Report("เข้าสู่ระบบไม่ได้", IssueImpact.Blocking, IssueModule.Login));
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        var export = await ReportAsync(w, w.StaffA, Report("ส่งออก Excel ช้ามาก", IssueImpact.Minor, IssueModule.Export));
        await w.Issues.AssignAsync(login.Id, w.ItA.Id, w.ItB.Id);

        async Task<List<string>> Numbers(IssueQuery q) =>
            (await w.Issues.ListAsync(q, w.ItA.Id)).Items.Select(i => i.Number).ToList();

        Assert.Equal([login.Number], await Numbers(new IssueQuery(Module: IssueModule.Login)));
        Assert.Equal([export.Number], await Numbers(new IssueQuery(Search: "excel")));
        Assert.Equal([login.Number], await Numbers(new IssueQuery(Search: "ISS-2569-0001")));
        Assert.Equal([login.Number], await Numbers(new IssueQuery(AssigneeId: w.ItA.Id)));
        Assert.Equal([login.Number], await Numbers(new IssueQuery(Status: IssueStatus.Assigned)));
        Assert.Equal([login.Number, export.Number], await Numbers(new IssueQuery(Sort: "priority")));
        Assert.Equal([login.Number, export.Number], await Numbers(new IssueQuery(Sort: "oldest")));
        Assert.Equal([export.Number, login.Number], await Numbers(new IssueQuery()));          // newest first
        Assert.Empty(await Numbers(new IssueQuery(OpenOnly: false)));
    }

    [Fact]
    public async Task Pages_report_the_total_and_slice_the_results()
    {
        var w = await TestWorld.CreateAsync();
        for (var i = 0; i < 5; i++) await ReportAsync(w, w.StaffA, Report($"ปัญหาข้อที่ {i + 1} ของระบบ"));

        var page = await w.Issues.ListAsync(new IssueQuery(Page: 2, PageSize: 2), w.ItA.Id);

        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.Page);
    }

    [Fact]
    public async Task The_assignee_list_shows_IT_with_their_workload()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.AssignAsync(issue.Id, w.ItA.Id, w.ItB.Id);

        var assignees = await w.Issues.AssigneesAsync(w.ItB.Id);

        Assert.Contains(assignees, a => a.Id == w.ItA.Id && a.OpenIssues == 1);
        Assert.Contains(assignees, a => a.Id == w.ItB.Id && a.OpenIssues == 0);
        Assert.DoesNotContain(assignees, a => a.Id == w.StaffA.Id);
        Assert.Equal(assignees.OrderBy(a => a.OpenIssues).Select(a => a.OpenIssues), assignees.Select(a => a.OpenIssues)); // lightest load first
        Assert.Equal(w.ItA.Id, assignees[^1].Id);                                                                          // the busy one is last
    }

    // ───────────── Allowed actions follow the person and the status ─────────────

    [Fact]
    public async Task The_actions_offered_depend_on_who_is_looking_and_where_the_issue_is()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);

        var reporter = (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions;
        Assert.Contains(IssueActionNames.Comment, reporter);
        Assert.DoesNotContain(IssueActionNames.Assign, reporter);
        Assert.DoesNotContain(IssueActionNames.InternalNote, reporter);

        var it = (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).AllowedActions;
        Assert.Contains(IssueActionNames.Assign, it);
        Assert.Contains(IssueActionNames.Start, it);
        Assert.Contains(IssueActionNames.Reject, it);
        Assert.Contains(IssueActionNames.InternalNote, it);
        Assert.DoesNotContain(IssueActionNames.Resolve, it);       // not picked up yet

        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await ResolveAsync(w, issue.Id, w.ItA);

        var afterFix = (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions;
        Assert.Contains(IssueActionNames.Confirm, afterFix);
        Assert.Contains(IssueActionNames.Reopen, afterFix);
    }

    // ───────────── Never fail the work because of a notification ─────────────

    [Fact]
    public async Task A_broken_hub_or_mail_provider_never_fails_the_operation()
    {
        var w = await TestWorld.CreateAsync();
        w.Notifier.Throw = true;
        w.Email.Throw = true;

        var issue = await ReportAsync(w, w.StaffA);
        var started = await w.Issues.StartAsync(issue.Id, w.ItA.Id);

        Assert.Equal(IssueStatus.InProgress, started.Status);
    }

    [Fact]
    public async Task E_mail_can_be_switched_off_while_live_updates_continue()
    {
        var w = await TestWorld.CreateAsync(emailEnabled: false);

        await ReportAsync(w, w.StaffA);

        Assert.Empty(w.Email.IssueSent);
        Assert.NotEmpty(w.Notifier.Notifications);
    }

    // ───────────── Reporting without signing in ─────────────

    private static PublicIssueRequest PublicReport(
        string name = "สมหญิง ใจดี", string? email = "somying@example.com", string? phone = null, Guid? department = null) =>
        new(name, email, phone, department, "เข้าสู่ระบบไม่ได้เลย", "ใส่รหัสผ่านถูกแล้วแต่ขึ้นว่าไม่ถูกต้อง",
            IssueCategory.Bug, IssueModule.Login, IssueImpact.Blocking);

    [Fact]
    public async Task Someone_who_cannot_sign_in_can_still_report_and_gets_a_receipt_and_an_e_mail()
    {
        var w = await TestWorld.CreateAsync();

        var receipt = await w.Issues.CreatePublicAsync(PublicReport(department: w.DeptA.Id), NoFiles, "203.0.113.9");

        Assert.Equal("ISS-2569-0001", receipt.Number);
        Assert.Equal(w.Clock.UtcNow.AddHours(2), receipt.ResponseDueAt);

        var stored = await w.Db.Issues.SingleAsync();
        Assert.True(stored.IsPublicSubmission);
        Assert.Null(stored.ReporterUserId);
        Assert.Equal("203.0.113.9", stored.ReporterIp);
        Assert.Equal(w.DeptA.Id, stored.DepartmentId);

        Assert.Equal(["somying@example.com"], w.Email.To(IssueEmailKind.Received));
        Assert.Equivalent(new[] { w.ItA.Email, w.ItB.Email }, w.Email.To(IssueEmailKind.NewForIt).ToArray());
        var audit = await w.Db.AuditLogs.SingleAsync(a => a.Action == "Create");
        Assert.Equal("somying@example.com", audit.UserEmail);
        Assert.Equal("สมหญิง ใจดี", audit.UserName);
    }

    [Fact]
    public async Task A_public_report_needs_a_name_and_a_way_to_reach_the_reporter()
    {
        var w = await TestWorld.CreateAsync();

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreatePublicAsync(PublicReport(name: "x"), NoFiles, null));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreatePublicAsync(PublicReport(email: null, phone: null), NoFiles, null));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreatePublicAsync(PublicReport(email: "not-an-email"), NoFiles, null));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreatePublicAsync(PublicReport(department: Guid.NewGuid()), NoFiles, null));

        var withPhoneOnly = await w.Issues.CreatePublicAsync(PublicReport(email: null, phone: "081-234-5678"), NoFiles, null);
        Assert.NotNull(withPhoneOnly.Number);
        Assert.Empty(w.Email.To(IssueEmailKind.Received)); // nobody to e-mail
    }

    [Fact]
    public async Task A_public_report_can_carry_screenshots_and_reaches_IT_with_them()
    {
        var w = await TestWorld.CreateAsync();

        await w.Issues.CreatePublicAsync(PublicReport(), [new IssueUpload("login.png", "image/png", Png)], null);

        var attachment = await w.Db.IssueAttachments.SingleAsync();
        var asIt = await w.Issues.GetAsync(attachment.IssueId, w.ItA.Id);
        Assert.Single(asIt.Attachments);
        Assert.Equal(IssueModule.Login, asIt.Module);
    }
}
