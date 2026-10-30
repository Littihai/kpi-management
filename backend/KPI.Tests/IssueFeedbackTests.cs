using KPI.Application.Issues;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

/// <summary>The reporter rates the fix; IT reads the ratings.</summary>
public class IssueFeedbackTests
{
    private static CreateIssueRequest Report() =>
        new("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง", IssueCategory.Bug, IssueModule.Application, IssueImpact.Degraded);

    private static async Task<IssueDetailDto> ReportAsync(TestWorld w, User by) =>
        await w.Issues.CreateAsync(Report(), [], by.Id, "10.1.2.3");

    /// <summary>Reported by <paramref name="by"/>, started and resolved by <paramref name="it"/>.</summary>
    private static async Task<IssueDetailDto> ResolvedAsync(TestWorld w, User by, User it)
    {
        var issue = await ReportAsync(w, by);
        await w.Issues.StartAsync(issue.Id, it.Id);
        return await w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("เปลี่ยนสายสัญญาณแล้ว", IssueRootCause.Infrastructure), it.Id);
    }

    [Fact]
    public async Task The_reporter_rates_a_resolved_issue_and_sees_the_rating_and_IT_hears_about_it()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);
        Assert.Contains(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions);
        Assert.DoesNotContain(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).AllowedActions);

        w.Notifier.Notifications.Clear();
        var rated = await w.Issues.FeedbackAsync(issue.Id, 4, "  เร็วดี  ", w.StaffA.Id);

        Assert.Equal(4, rated.Feedback!.Rating);
        Assert.Equal("เร็วดี", rated.Feedback.Note);
        Assert.DoesNotContain(IssueActionNames.Feedback, rated.AllowedActions);

        var row = await w.Db.IssueFeedbacks.SingleAsync();
        Assert.Equal(issue.Id, row.IssueId);
        Assert.Equal(w.StaffA.Id, row.RaterUserId);
        Assert.Equal(w.ItA.Id, row.AssigneeId);

        // The assignee is told, in the bell as well as live, and neither the history nor the event carries the score.
        var (toast, audience) = Assert.Single(w.Notifier.Notifications);
        Assert.Equal("ผู้แจ้งให้คะแนนความพึงพอใจ", toast.Title);
        Assert.Equal([w.ItA.Id], audience.UserIds);
        var entry = Assert.Single((await w.Notifications.ListAsync(new KPI.Application.Notifications.NotificationQuery(), w.ItA.Id)).Items, n => n.Kind == "feedback");
        Assert.Equal($"{issue.Number} · {issue.Title}", entry.Message);
        Assert.Equal("feedback", w.Notifier.IssueEvents.Last().Action);
        var audit = await w.Db.AuditLogs.SingleAsync(a => a.Action == "Feedback");
        Assert.Null(audit.OldValues);
        Assert.Null(audit.NewValues);
    }

    [Fact]
    public async Task A_closed_issue_can_be_rated_too()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.ConfirmAsync(issue.Id, w.StaffA.Id);

        var rated = await w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffA.Id);

        Assert.Equal(IssueStatus.Closed, rated.Status);
        Assert.Equal(5, rated.Feedback!.Rating);
        Assert.Null(rated.Feedback.Note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task A_rating_outside_one_to_five_is_refused(int rating)
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.FeedbackAsync(issue.Id, rating, null, w.StaffA.Id));
        Assert.Empty(w.Db.IssueFeedbacks);
    }

    [Fact]
    public async Task The_note_may_be_a_thousand_characters_but_not_more()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.FeedbackAsync(issue.Id, 3, new string('ก', 1001), w.StaffA.Id));
        var rated = await w.Issues.FeedbackAsync(issue.Id, 3, new string('ก', 1000), w.StaffA.Id);

        Assert.Equal(1000, rated.Feedback!.Note!.Length);
    }

    [Fact]
    public async Task Only_the_reporter_may_rate_and_nobody_outside_their_scope_learns_the_issue_exists()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);

        foreach (var other in new[] { w.ManagerA, w.ItA, w.SuperAdmin })
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, other.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffB.Id));
        Assert.Empty(w.Db.IssueFeedbacks);
    }

    [Fact]
    public async Task An_issue_from_the_public_form_cannot_be_rated()
    {
        var w = await TestWorld.CreateAsync();
        await w.Issues.CreatePublicAsync(
            new PublicIssueRequest("สมหญิง", "somying@example.com", null, w.DeptA.Id, Report().Title, Report().Description,
                IssueCategory.Bug, IssueModule.Application, IssueImpact.Degraded), [], "10.9.9.9");
        var issue = await w.Db.Issues.SingleAsync();
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("เปลี่ยนสายสัญญาณแล้ว", IssueRootCause.Infrastructure), w.ItA.Id);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, w.ItA.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffA.Id));   // plain staff cannot even open it
        Assert.DoesNotContain(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).AllowedActions);
    }

    [Fact]
    public async Task An_issue_that_is_not_fixed_yet_or_was_rejected_cannot_be_rated()
    {
        var w = await TestWorld.CreateAsync();
        var fresh = await ReportAsync(w, w.StaffA);
        var working = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(working.Id, w.ItA.Id);
        var rejected = await ReportAsync(w, w.StaffA);
        await w.Issues.RejectAsync(rejected.Id, "ซ้ำกับเรื่องอื่น", null, w.ItA.Id);

        foreach (var id in new[] { fresh.Id, working.Id, rejected.Id })
        {
            await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.FeedbackAsync(id, 5, null, w.StaffA.Id));
            Assert.DoesNotContain(IssueActionNames.Feedback, (await w.Issues.GetAsync(id, w.StaffA.Id)).AllowedActions);
        }
    }

    [Fact]
    public async Task The_rating_window_closes_thirty_days_after_the_fix()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);

        w.Clock.Advance(TimeSpan.FromDays(30));
        Assert.Contains(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions);

        w.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffA.Id));
    }

    [Fact]
    public async Task A_second_rating_is_refused_until_the_issue_is_reopened_and_fixed_again_and_then_replaces_the_first()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(issue.Id, 2, "ยังไม่หาย", w.StaffA.Id);

        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffA.Id));

        w.Clock.Advance(TimeSpan.FromHours(1));
        await w.Issues.ReopenAsync(issue.Id, "ยังพิมพ์ไม่ออก", w.StaffA.Id);
        w.Clock.Advance(TimeSpan.FromHours(1));
        await w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("เปลี่ยนเครื่องพิมพ์ใหม่", IssueRootCause.Infrastructure), w.ItA.Id);

        Assert.Contains(IssueActionNames.Feedback, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).AllowedActions);
        var again = await w.Issues.FeedbackAsync(issue.Id, 5, null, w.StaffA.Id);

        Assert.Equal(5, again.Feedback!.Rating);
        Assert.Null(again.Feedback.Note);
        Assert.Equal(5, (await w.Db.IssueFeedbacks.SingleAsync()).Rating);
        await Assert.ThrowsAsync<InvalidStateException>(() => w.Issues.FeedbackAsync(issue.Id, 1, null, w.StaffA.Id));
    }

    [Fact]
    public async Task The_rating_is_shown_to_the_reporter_and_to_IT_and_to_nobody_else()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(issue.Id, 4, "ดี", w.StaffA.Id);

        Assert.Equal(4, (await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).Feedback!.Rating);
        Assert.Equal(4, (await w.Issues.GetAsync(issue.Id, w.ItA.Id)).Feedback!.Rating);
        Assert.Equal(4, (await w.Issues.GetAsync(issue.Id, w.SuperAdmin.Id)).Feedback!.Rating);
        Assert.Null((await w.Issues.GetAsync(issue.Id, w.ManagerA.Id)).Feedback);   // can open the issue (same department), not the rating
    }

    [Fact]
    public async Task An_issue_nobody_rated_has_no_feedback()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ResolvedAsync(w, w.StaffA, w.ItA);

        Assert.Null((await w.Issues.GetAsync(issue.Id, w.StaffA.Id)).Feedback);
        Assert.Null((await w.Issues.GetAsync(issue.Id, w.ItA.Id)).Feedback);
    }

    [Fact]
    public async Task My_feedback_lists_only_the_callers_own_issues_that_were_rated_or_can_still_be_rated()
    {
        var w = await TestWorld.CreateAsync();
        var rated = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(rated.Id, 3, "พอใช้", w.StaffA.Id);
        var waiting = await ResolvedAsync(w, w.StaffA, w.ItA);
        await ReportAsync(w, w.StaffA);                                  // not fixed: not listed
        var theirs = await ResolvedAsync(w, w.StaffB, w.ItA);            // someone else's
        var expired = await ResolvedAsync(w, w.StaffA, w.ItA);
        // expired: fixed long ago, never rated
        var old = await w.Db.Issues.SingleAsync(i => i.Id == expired.Id);
        old.ResolvedAt = w.Clock.UtcNow.AddDays(-40);
        await w.Db.SaveChangesAsync();

        var mine = await w.Issues.MyFeedbackAsync(w.StaffA.Id);

        Assert.Equal(new[] { rated.Id, waiting.Id }.Order(), mine.Select(m => m.IssueId).Order());
        var ratedItem = mine.Single(m => m.IssueId == rated.Id);
        Assert.Equal(3, ratedItem.Rating);
        Assert.Equal("พอใช้", ratedItem.Note);
        Assert.False(ratedItem.CanRate);
        var waitingItem = mine.Single(m => m.IssueId == waiting.Id);
        Assert.Null(waitingItem.Rating);
        Assert.True(waitingItem.CanRate);

        Assert.Equal([theirs.Id], (await w.Issues.MyFeedbackAsync(w.StaffB.Id)).Select(m => m.IssueId));
        Assert.Empty(await w.Issues.MyFeedbackAsync(w.ItA.Id));
    }

    [Fact]
    public async Task The_report_matches_a_hand_computed_set()
    {
        var w = await TestWorld.CreateAsync();
        var a1 = await ResolvedAsync(w, w.StaffA, w.ItA);
        var a2 = await ResolvedAsync(w, w.StaffA, w.ItA);
        var b1 = await ResolvedAsync(w, w.StaffB, w.ItB);
        await ResolvedAsync(w, w.StaffB, w.ItB);   // never rated
        await w.Issues.FeedbackAsync(a1.Id, 5, null, w.StaffA.Id);
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        await w.Issues.FeedbackAsync(a2.Id, 2, "ช้า", w.StaffA.Id);
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        await w.Issues.FeedbackAsync(b1.Id, 4, null, w.StaffB.Id);

        var report = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), w.ItA.Id);

        Assert.Equal(3, report.Count);
        Assert.Equal((5 + 2 + 4) / 3.0, report.Average!.Value, 6);
        Assert.Equal([0, 1, 0, 1, 1], report.Distribution);
        var perAssignee = report.ByAssignee.ToDictionary(a => a.AssigneeId!.Value);
        Assert.Equal(2, perAssignee[w.ItA.Id].Count);
        Assert.Equal(3.5, perAssignee[w.ItA.Id].Average, 6);
        Assert.Equal(1, perAssignee[w.ItB.Id].Count);
        Assert.Equal(4.0, perAssignee[w.ItB.Id].Average, 6);
        Assert.Equal(w.ItA.FullName, perAssignee[w.ItA.Id].AssigneeName);

        Assert.Equal(3, report.Items.Total);
        Assert.Equal([b1.Id, a2.Id, a1.Id], report.Items.Items.Select(i => i.IssueId));   // newest rating first
        Assert.Equal("ช้า", report.Items.Items[1].Note);
        Assert.Equal(w.ItB.FullName, report.Items.Items[0].AssigneeName);
    }

    [Fact]
    public async Task The_report_can_be_limited_to_a_date_range()
    {
        var w = await TestWorld.CreateAsync();
        var first = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(first.Id, 1, null, w.StaffA.Id);
        w.Clock.Advance(TimeSpan.FromDays(2));
        var second = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(second.Id, 5, null, w.StaffA.Id);

        var recent = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(From: w.Clock.UtcNow.AddDays(-1)), w.ItA.Id);
        var early = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(To: w.Clock.UtcNow.AddDays(-1)), w.ItA.Id);

        Assert.Equal(1, recent.Count);
        Assert.Equal(5.0, recent.Average);
        Assert.Equal(1, early.Count);
        Assert.Equal(1.0, early.Average);
    }

    [Fact]
    public async Task With_nothing_rated_the_report_is_empty_and_has_no_average()
    {
        var w = await TestWorld.CreateAsync();
        await ResolvedAsync(w, w.StaffA, w.ItA);

        var report = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), w.ItA.Id);

        Assert.Equal(0, report.Count);
        Assert.Null(report.Average);
        Assert.Equal([0, 0, 0, 0, 0], report.Distribution);
        Assert.Empty(report.ByAssignee);
        Assert.Empty(report.Items.Items);
    }

    [Fact]
    public async Task Only_IT_may_open_the_report()
    {
        var w = await TestWorld.CreateAsync();

        foreach (var other in new[] { w.StaffA, w.ManagerA, w.Director, w.ViewerA })
            await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), other.Id));
        await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), w.SuperAdmin.Id);
    }

    [Fact]
    public async Task A_role_that_sees_only_its_own_issues_sees_only_its_own_ratings_in_the_report()
    {
        var w = await TestWorld.CreateAsync();
        var role = new Role { Name = "Own IT", Description = "ทดสอบ", IssueVisibility = IssueVisibility.Own };
        foreach (var name in new[] { Permissions.Issue.View, Permissions.Issue.Create, Permissions.Issue.Manage })
            role.RolePermissions.Add(new RolePermission { Permission = await w.Db.Permissions.SingleAsync(p => p.Name == name) });
        w.Db.Roles.Add(role);
        await w.Db.SaveChangesAsync();
        var own = await w.AddUserAsync("own-it", role.Name, w.DeptA.Id);

        var theirs = await ResolvedAsync(w, own, w.ItA);
        var others = await ResolvedAsync(w, w.StaffA, w.ItA);
        await w.Issues.FeedbackAsync(theirs.Id, 5, null, own.Id);
        await w.Issues.FeedbackAsync(others.Id, 1, null, w.StaffA.Id);

        var scoped = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), own.Id);
        var all = await w.Issues.FeedbackReportAsync(new FeedbackReportQuery(), w.ItA.Id);

        Assert.Equal(1, scoped.Count);
        Assert.Equal(5.0, scoped.Average);
        Assert.Equal([theirs.Id], scoped.Items.Items.Select(i => i.IssueId));
        Assert.Equal(2, all.Count);
    }
}
