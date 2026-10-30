using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Issues;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using Microsoft.EntityFrameworkCore;

namespace KPI.Tests;

/// <summary>What is waiting for each person: the number behind the red badge in the app.</summary>
public class IssueAttentionTests
{
    private static CreateIssueRequest Report(IssueImpact impact = IssueImpact.Degraded) =>
        new("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง", IssueCategory.Bug, IssueModule.Application, impact);

    private static async Task<IssueDetailDto> ReportAsync(TestWorld w, User by) =>
        await w.Issues.CreateAsync(Report(), [], by.Id, "10.1.2.3");

    private static Task<IssueDetailDto> ResolveAsync(TestWorld w, Guid id, User by) =>
        w.Issues.ResolveAsync(id, new ResolveIssueRequest("เปลี่ยนสายสัญญาณแล้ว", IssueRootCause.Infrastructure), by.Id);

    private static async Task<IssueAttentionDto> For(TestWorld w, User who) => await w.Issues.AttentionAsync(who.Id);

    private static IssueAttentionDto Counts(
        int total, int newInQueue = 0, int assignedToMe = 0, int overdue = 0, int waitingForMe = 0, int toConfirm = 0) =>
        new(total, newInQueue, assignedToMe, overdue, waitingForMe, toConfirm);

    private static readonly IssueAttentionDto Nothing = Counts(0);

    [Fact]
    public async Task Nothing_is_waiting_for_anyone_before_the_first_report()
    {
        var w = await TestWorld.CreateAsync();

        foreach (var user in new[] { w.SuperAdmin, w.Director, w.ManagerA, w.StaffA, w.ItA })
            Assert.Equal(Nothing, await For(w, user));
    }

    [Fact]
    public async Task A_new_report_waits_in_the_queue_for_everyone_in_IT_and_for_nobody_else()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA);

        var inQueue = Counts(1, newInQueue: 1);
        Assert.Equal(inQueue, await For(w, w.ItA));
        Assert.Equal(inQueue, await For(w, w.ItB));
        Assert.Equal(inQueue, await For(w, w.SuperAdmin));   // can manage issues, so works the queue as well

        // The reporter, the manager of the same department and the director can all see it, but it is not waiting for them.
        foreach (var user in new[] { w.StaffA, w.ManagerA, w.Director, w.StaffB, w.ManagerB })
            Assert.Equal(Nothing, await For(w, user));
    }

    [Fact]
    public async Task An_issue_that_meets_two_rules_counts_once_in_the_total()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA);                          // never answered
        var assigned = await ReportAsync(w, w.StaffA);
        await w.Issues.AssignAsync(assigned.Id, w.ItA.Id, w.ItB.Id);
        w.Clock.Advance(TimeSpan.FromHours(3));                  // the first one has now waited past the two-hour target
        await ReportAsync(w, w.StaffB);

        // The unanswered one is unclaimed and overdue (two rules, one issue); the assigned one is ItA's and on time.
        Assert.Equal(Counts(3, newInQueue: 2, assignedToMe: 1, overdue: 1), await For(w, w.ItA));
        Assert.Equal(Counts(2, newInQueue: 2, overdue: 1), await For(w, w.ItB));

        // Three days on (the medium target) every one of them is late, and the assigned one is overdue and ItA's at once.
        w.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(Counts(3, newInQueue: 2, assignedToMe: 1, overdue: 3), await For(w, w.ItA));
        Assert.Equal(Counts(3, newInQueue: 2, overdue: 3), await For(w, w.ItB));
    }

    [Fact]
    public async Task An_issue_moves_between_desks_as_it_goes_through_its_life()
    {
        var w = await TestWorld.CreateAsync();
        var id = (await ReportAsync(w, w.StaffA)).Id;

        await w.Issues.AssignAsync(id, w.ItA.Id, w.ItB.Id);
        Assert.Equal(Counts(1, assignedToMe: 1), await For(w, w.ItA));
        Assert.Equal(Nothing, await For(w, w.ItB));            // it has left the queue
        Assert.Equal(Nothing, await For(w, w.StaffA));

        await w.Issues.StartAsync(id, w.ItA.Id);
        Assert.Equal(Counts(1, assignedToMe: 1), await For(w, w.ItA));

        // Asking the reporter a question hands the issue to them; the answer hands it back.
        await w.Issues.RequestInfoAsync(id, "ใช้เบราว์เซอร์อะไรอยู่", w.ItA.Id);
        Assert.Equal(Counts(1, waitingForMe: 1), await For(w, w.StaffA));
        Assert.Equal(Nothing, await For(w, w.ItA));
        await w.Issues.CommentAsync(id, "ใช้ Chrome ครับ", false, w.StaffA.Id);
        Assert.Equal(Nothing, await For(w, w.StaffA));
        Assert.Equal(Counts(1, assignedToMe: 1), await For(w, w.ItA));

        // Parked for a spare part, it is nobody's move.
        await w.Issues.HoldAsync(id, "รออะไหล่", w.ItA.Id);
        Assert.Equal(Nothing, await For(w, w.ItA));
        await w.Issues.ResumeAsync(id, "อะไหล่มาแล้ว", w.ItA.Id);
        Assert.Equal(Counts(1, assignedToMe: 1), await For(w, w.ItA));

        // Fixed: the reporter is asked to confirm. Not fixed after all: back on the assignee's desk.
        await ResolveAsync(w, id, w.ItA);
        Assert.Equal(Counts(1, toConfirm: 1), await For(w, w.StaffA));
        Assert.Equal(Nothing, await For(w, w.ItA));
        await w.Issues.ReopenAsync(id, "ยังไม่หาย", w.StaffA.Id);
        Assert.Equal(Nothing, await For(w, w.StaffA));
        Assert.Equal(Counts(1, assignedToMe: 1), await For(w, w.ItA));

        await ResolveAsync(w, id, w.ItA);
        await w.Issues.ConfirmAsync(id, w.StaffA.Id);
        foreach (var user in new[] { w.StaffA, w.ItA, w.ItB, w.SuperAdmin })
            Assert.Equal(Nothing, await For(w, user));
    }

    [Fact]
    public async Task People_who_do_not_work_the_queue_are_never_told_about_unclaimed_or_overdue_issues()
    {
        var w = await TestWorld.CreateAsync();
        var id = (await ReportAsync(w, w.StaffA)).Id;
        w.Clock.Advance(TimeSpan.FromDays(10));

        // The manager really can see it ...
        Assert.Equal(id, Assert.Single((await w.Issues.ListAsync(new IssueQuery(), w.ManagerA.Id)).Items).Id);
        // ... but a late issue is IT's to chase, not theirs.
        foreach (var user in new[] { w.ManagerA, w.Director, w.TeamLeaderA, w.ViewerA, w.StaffA })
            Assert.Equal(Nothing, await For(w, user));
        Assert.Equal(Counts(1, newInQueue: 1, overdue: 1), await For(w, w.ItA));
    }

    [Fact]
    public async Task A_manager_hears_about_their_own_reports_like_anyone_else()
    {
        var w = await TestWorld.CreateAsync();
        var id = (await ReportAsync(w, w.ManagerA)).Id;
        await w.Issues.AssignAsync(id, w.ItA.Id, w.ItA.Id);
        await w.Issues.RequestInfoAsync(id, "เกิดขึ้นกับเครื่องไหน", w.ItA.Id);

        Assert.Equal(Counts(1, waitingForMe: 1), await For(w, w.ManagerA));
        await w.Issues.CommentAsync(id, "เครื่องหน้าห้อง", false, w.ManagerA.Id);
        await ResolveAsync(w, id, w.ItA);
        Assert.Equal(Counts(1, toConfirm: 1), await For(w, w.ManagerA));
        Assert.Equal(Nothing, await For(w, w.Director));
    }

    [Fact]
    public async Task A_queue_worker_is_only_told_about_issues_their_role_lets_them_see()
    {
        var w = await TestWorld.CreateAsync();
        var role = new Role { Name = "Local IT", Description = "ทดสอบ", IssueVisibility = IssueVisibility.Department };
        foreach (var name in new[] { Permissions.Issue.View, Permissions.Issue.Manage })
            role.RolePermissions.Add(new RolePermission { Permission = await w.Db.Permissions.SingleAsync(p => p.Name == name) });
        w.Db.Roles.Add(role);
        await w.Db.SaveChangesAsync();
        var local = await w.AddUserAsync("local-it", role.Name, w.DeptA.Id);

        await ReportAsync(w, w.StaffA);   // their department
        await ReportAsync(w, w.StaffB);   // another department

        Assert.Equal(Counts(1, newInQueue: 1), await For(w, local));
        Assert.Equal(Counts(2, newInQueue: 2), await For(w, w.ItA));
    }

    [Fact]
    public async Task Finished_issues_wait_for_nobody_however_late_they_are()
    {
        var w = await TestWorld.CreateAsync();
        var rejected = await ReportAsync(w, w.StaffA);
        var closed = await ReportAsync(w, w.StaffA);
        await w.Issues.RejectAsync(rejected.Id, "ซ้ำกับเรื่องอื่น", null, w.ItA.Id);
        await w.Issues.StartAsync(closed.Id, w.ItA.Id);
        await ResolveAsync(w, closed.Id, w.ItA);
        await w.Issues.ConfirmAsync(closed.Id, w.StaffA.Id);

        w.Clock.Advance(TimeSpan.FromDays(30));

        foreach (var user in new[] { w.StaffA, w.ItA, w.ItB, w.SuperAdmin })
            Assert.Equal(Nothing, await For(w, user));
    }

    [Fact]
    public async Task Someone_who_may_not_view_issues_or_cannot_sign_in_is_refused()
    {
        var w = await TestWorld.CreateAsync();
        w.Db.Roles.Add(new Role { Name = "No issues", Description = "ทดสอบ" });
        await w.Db.SaveChangesAsync();
        var outsider = await w.AddUserAsync("outsider", "No issues", w.DeptA.Id);
        var retired = await w.AddUserAsync("retired", RoleNames.Staff, w.DeptA.Id, active: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AttentionAsync(outsider.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AttentionAsync(retired.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Issues.AttentionAsync(Guid.NewGuid()));
    }
}

/// <summary>The ready-made lists: each one holds exactly the issues the matching number counts.</summary>
public class IssueAttentionViewTests
{
    private static CreateIssueRequest Report() =>
        new("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง", IssueCategory.Bug, IssueModule.Application, IssueImpact.Degraded);

    private static async Task<IssueDetailDto> ReportAsync(TestWorld w, User by) =>
        await w.Issues.CreateAsync(Report(), [], by.Id, "10.1.2.3");

    private static async Task<int> Total(TestWorld w, User who, IssueQuery query) =>
        (await w.Issues.ListAsync(query with { PageSize = 100 }, who.Id)).Total;

    /// <summary>A mix that touches every rule: unclaimed, claimed, waiting for the reporter, fixed, late.</summary>
    private static async Task<TestWorld> BusyWorldAsync()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA);                                                       // never answered, will be late
        var claimed = await ReportAsync(w, w.StaffA);
        await w.Issues.AssignAsync(claimed.Id, w.ItA.Id, w.ItB.Id);
        var asked = await ReportAsync(w, w.StaffA);
        await w.Issues.AssignAsync(asked.Id, w.ItA.Id, w.ItB.Id);
        await w.Issues.RequestInfoAsync(asked.Id, "ใช้เบราว์เซอร์อะไรอยู่", w.ItA.Id);
        var fixedOne = await ReportAsync(w, w.StaffA);
        await w.Issues.AssignAsync(fixedOne.Id, w.ItB.Id, w.ItB.Id);
        await w.Issues.ResolveAsync(fixedOne.Id, new ResolveIssueRequest("เปลี่ยนสายสัญญาณแล้ว", IssueRootCause.Infrastructure), w.ItB.Id);
        w.Clock.Advance(TimeSpan.FromHours(3));
        await ReportAsync(w, w.StaffB);
        return w;
    }

    [Fact]
    public async Task Every_view_lists_exactly_as_many_issues_as_the_badge_counts_for_IT_and_for_a_requester()
    {
        var w = await BusyWorldAsync();

        foreach (var who in new[] { w.ItA, w.ItB, w.SuperAdmin, w.StaffA, w.StaffB, w.ManagerA })
        {
            var counts = await w.Issues.AttentionAsync(who.Id);
            Assert.Equal(counts.Total, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.Any)));
            Assert.Equal(counts.NewInQueue, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.NewInQueue)));
            Assert.Equal(counts.AssignedToMe, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.AssignedToMe)));
            Assert.Equal(counts.Overdue, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.Overdue)));
            Assert.Equal(counts.WaitingForMe, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.WaitingForMe)));
            Assert.Equal(counts.ToConfirm, await Total(w, who, new IssueQuery(Attention: IssueAttentionRule.ToConfirm)));
        }
    }

    [Fact]
    public async Task The_assigned_view_is_only_what_is_still_the_assignees_to_work_on()
    {
        var w = await BusyWorldAsync();

        var mine = (await w.Issues.ListAsync(new IssueQuery(Attention: IssueAttentionRule.AssignedToMe, Sort: "updated"), w.ItA.Id)).Items;

        Assert.Single(mine);                                            // the one waiting for the reporter is not theirs to work on right now
        Assert.All(mine, i => Assert.Equal(w.ItA.Id, i.AssigneeId));
        Assert.Empty((await w.Issues.ListAsync(new IssueQuery(Attention: IssueAttentionRule.AssignedToMe), w.StaffA.Id)).Items);
    }

    [Fact]
    public async Task A_requester_who_asks_for_a_queue_view_gets_an_empty_page()
    {
        var w = await BusyWorldAsync();
        w.Clock.Advance(TimeSpan.FromDays(10));

        foreach (var rule in new[] { IssueAttentionRule.NewInQueue, IssueAttentionRule.Overdue })
        {
            foreach (var who in new[] { w.StaffA, w.ManagerA, w.Director, w.ViewerA })
                Assert.Empty((await w.Issues.ListAsync(new IssueQuery(Attention: rule), who.Id)).Items);
            Assert.NotEmpty((await w.Issues.ListAsync(new IssueQuery(Attention: rule), w.ItA.Id)).Items);
        }
    }

    [Fact]
    public async Task A_department_role_never_sees_other_departments_or_public_form_rows_through_a_view()
    {
        var w = await TestWorld.CreateAsync();
        var role = new Role { Name = "Local IT", Description = "ทดสอบ", IssueVisibility = IssueVisibility.Department };
        foreach (var name in new[] { Permissions.Issue.View, Permissions.Issue.Manage })
            role.RolePermissions.Add(new RolePermission { Permission = await w.Db.Permissions.SingleAsync(p => p.Name == name) });
        w.Db.Roles.Add(role);
        await w.Db.SaveChangesAsync();
        var local = await w.AddUserAsync("local-it", role.Name, w.DeptA.Id);

        var own = await ReportAsync(w, w.StaffA);
        await ReportAsync(w, w.StaffB);
        await w.Issues.CreatePublicAsync(
            new PublicIssueRequest("สมหญิง ใจดี", "somying@example.com", null, w.DeptB.Id, "เข้าสู่ระบบไม่ได้เลย",
                "ใส่รหัสผ่านถูกแล้วแต่ขึ้นว่าไม่ถูกต้อง", IssueCategory.Bug, IssueModule.Login, IssueImpact.Blocking), [], null);
        w.Clock.Advance(TimeSpan.FromDays(10));

        foreach (var query in new[]
                 {
                     new IssueQuery(Attention: IssueAttentionRule.Any), new IssueQuery(Attention: IssueAttentionRule.NewInQueue),
                     new IssueQuery(Attention: IssueAttentionRule.Overdue), new IssueQuery(Sla: IssueSlaState.Near),
                     new IssueQuery(Sla: IssueSlaState.Overdue),
                 })
            Assert.Equal(own.Id, Assert.Single((await w.Issues.ListAsync(query, local.Id)).Items).Id);
    }

    [Fact]
    public async Task The_near_view_and_the_summary_number_agree_and_include_what_is_already_late()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA);                                  // response due in 2 hours, resolve in 3 days
        Assert.Equal(0, (await w.Issues.SummaryAsync(w.ItA.Id)).NearSla);
        Assert.Equal(0, await Total(w, w.ItA, new IssueQuery(Sla: IssueSlaState.Near)));

        w.Clock.Advance(TimeSpan.FromHours(1.6));                        // inside the last 30 minutes of the response target
        Assert.Equal(1, (await w.Issues.SummaryAsync(w.ItA.Id)).NearSla);
        Assert.Equal(0, await Total(w, w.ItA, new IssueQuery(Sla: IssueSlaState.Overdue)));

        w.Clock.Advance(TimeSpan.FromHours(1));                          // now actually late
        var summary = await w.Issues.SummaryAsync(w.ItA.Id);
        Assert.Equal(1, summary.NearSla);
        Assert.Equal(1, summary.Overdue);
        Assert.Equal(summary.NearSla, await Total(w, w.ItA, new IssueQuery(Sla: IssueSlaState.Near)));
        Assert.Equal(summary.Overdue, await Total(w, w.ItA, new IssueQuery(Sla: IssueSlaState.Overdue)));

        // The reporter sees their own issue the same way (the numbers are about what a person may see, not who works the queue).
        Assert.Equal(1, (await w.Issues.SummaryAsync(w.StaffA.Id)).NearSla);
        Assert.Equal(0, (await w.Issues.SummaryAsync(w.StaffB.Id)).NearSla);
    }
}

/// <summary>The same numbers over HTTP, with real sign-ins: the shape the apps rely on and who may ask.</summary>
public class IssueAttentionApiTests : IClassFixture<IssueApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly IssueApiFactory _factory;

    public IssueAttentionApiTests(IssueApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> AttentionOf(string user)
    {
        var response = await (await _factory.ClientAsync(user)).GetAsync("/api/issues/attention");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task AssertCounts(string user, int total, int newInQueue = 0, int assignedToMe = 0, int overdue = 0, int waitingForMe = 0, int toConfirm = 0)
    {
        var body = await AttentionOf(user);
        Assert.True(
            body.GetProperty("total").GetInt32() == total
            && body.GetProperty("newInQueue").GetInt32() == newInQueue
            && body.GetProperty("assignedToMe").GetInt32() == assignedToMe
            && body.GetProperty("overdue").GetInt32() == overdue
            && body.GetProperty("waitingForMe").GetInt32() == waitingForMe
            && body.GetProperty("toConfirm").GetInt32() == toConfirm,
            $"{user}: {body}");
    }

    private static MultipartFormDataContent ReportForm() => new()
    {
        { new StringContent("เข้าระบบไม่ได้ตั้งแต่เช้า"), "Title" },
        { new StringContent("กดปุ่มเข้าสู่ระบบแล้วไม่มีอะไรเกิดขึ้นเลยทั้งเช้าและบ่าย"), "Description" },
        { new StringContent("Bug"), "Category" },
        { new StringContent("Login"), "Module" },
        { new StringContent("Degraded"), "Impact" },
    };

    [Fact]
    public async Task The_response_has_exactly_the_agreed_fields_in_camel_case()
    {
        var body = await AttentionOf("staff-a");

        Assert.Equal(
            ["assignedToMe", "newInQueue", "overdue", "toConfirm", "total", "waitingForMe"],
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(body.EnumerateObject(), p => Assert.Equal(JsonValueKind.Number, p.Value.ValueKind));
    }

    [Fact]
    public async Task One_report_is_followed_from_the_queue_to_the_reporter_and_back_to_nothing()
    {
        var staff = await _factory.ClientAsync("staff-a");
        var it = await _factory.ClientAsync("it-a");
        foreach (var user in new[] { "staff-a", "manager-a", "it-a" })
            await AssertCounts(user, 0);

        var id = (await (await staff.PostAsync("/api/issues", ReportForm())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // In the queue: every queue worker is told; the reporter, a manager who can see it, other departments and the director are not.
        foreach (var user in new[] { "it-a", "it-b", "admin" })
            await AssertCounts(user, 1, newInQueue: 1);
        foreach (var user in new[] { "staff-a", "manager-a", "staff-b", "manager-b", "director" })
            await AssertCounts(user, 0);

        // Claimed: it leaves the queue and sits on the assignee's desk.
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsJsonAsync($"/api/issues/{id}/assign", new { assigneeId = _factory.UserId("it-a") })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsync($"/api/issues/{id}/start", null)).StatusCode);
        await AssertCounts("it-a", 1, assignedToMe: 1);
        await AssertCounts("it-b", 0);

        // A question goes to the reporter, and the answer comes back.
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsJsonAsync($"/api/issues/{id}/request-info", new { message = "ใช้เบราว์เซอร์อะไร" })).StatusCode);
        await AssertCounts("staff-a", 1, waitingForMe: 1);
        await AssertCounts("it-a", 0);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsJsonAsync($"/api/issues/{id}/comments", new { body = "ใช้ Chrome ครับ" })).StatusCode);
        await AssertCounts("staff-a", 0);
        await AssertCounts("it-a", 1, assignedToMe: 1);

        // Fixed, then confirmed.
        Assert.Equal(HttpStatusCode.OK, (await it.PostAsJsonAsync($"/api/issues/{id}/resolve",
            new { resolution = "ล้างแคชของเบราว์เซอร์แล้ว", rootCause = "Configuration" })).StatusCode);
        await AssertCounts("staff-a", 1, toConfirm: 1);
        await AssertCounts("it-a", 0);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/issues/{id}/confirm", null)).StatusCode);
        foreach (var user in new[] { "staff-a", "it-a", "it-b", "admin" })
            await AssertCounts(user, 0);
    }

    [Fact]
    public async Task Asking_without_signing_in_is_refused()
    {
        var response = await _factory.CreateClient().GetAsync("/api/issues/attention");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
