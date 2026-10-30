using KPI.Application.Issues;
using KPI.Domain.Entities;
using KPI.Domain.Issues;

namespace KPI.Tests;

public class QuickSearchTests
{
    private static readonly IReadOnlyList<IssueUpload> NoFiles = [];

    private static Task<IssueDetailDto> ReportAsync(TestWorld w, User by, string title) =>
        w.Issues.CreateAsync(new CreateIssueRequest(title, "อธิบายปัญหาให้ละเอียดพอสมควรสำหรับการทดสอบ", IssueCategory.Bug, IssueModule.Application, IssueImpact.Minor),
            NoFiles, by.Id, null);

    [Fact]
    public async Task Each_person_finds_only_what_the_issue_list_would_show_them()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA, "VPN ต่อไม่ได้ (A)");
        await ReportAsync(w, w.ManagerA, "VPN หลุดบ่อย (A)");
        await ReportAsync(w, w.StaffB, "VPN ช้ามาก (B)");

        var staff = await w.Issues.QuickSearchAsync("vpn", w.StaffA.Id);
        var manager = await w.Issues.QuickSearchAsync("vpn", w.ManagerA.Id);
        var it = await w.Issues.QuickSearchAsync("vpn", w.ItA.Id);

        Assert.Equal(["VPN ต่อไม่ได้ (A)"], staff.Select(h => h.Title));
        Assert.Equivalent(new[] { "VPN ต่อไม่ได้ (A)", "VPN หลุดบ่อย (A)" }, manager.Select(h => h.Title).ToArray());
        Assert.Equal(3, it.Count);
        Assert.Equal(it.Count, (await w.Issues.ListAsync(new IssueQuery(Search: "vpn"), w.ItA.Id)).Total);
    }

    [Fact]
    public async Task An_issue_number_in_either_calendar_style_finds_the_issue()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA, "เครื่องพิมพ์ชั้น 3 ไม่ทำงาน");
        await ReportAsync(w, w.StaffA, "จอภาพกะพริบ");

        var hit = Assert.Single(await w.Issues.QuickSearchAsync(issue.Number, w.StaffA.Id));
        Assert.Equal(issue.Id, hit.Id);
        Assert.Equal(issue.Number, hit.Number);
        Assert.Equal(issue.Id, Assert.Single(await w.Issues.QuickSearchAsync("2569 1", w.StaffA.Id)).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("v")]
    [InlineData(" v ")]
    public async Task Fewer_than_two_characters_return_nothing(string? term)
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA, "VPN ต่อไม่ได้");

        Assert.Empty(await w.Issues.QuickSearchAsync(term, w.StaffA.Id));
        var combined = await w.Search.SearchAsync(term, w.Director.Id);
        Assert.Empty(combined.Issues);
        Assert.Empty(combined.Users);
    }

    [Fact]
    public async Task Wildcard_characters_in_the_text_match_literally()
    {
        var w = await TestWorld.CreateAsync();
        await ReportAsync(w, w.StaffA, "ส่วนลด 50% ไม่คิด");
        await ReportAsync(w, w.StaffA, "ส่วนลด 5000 บาท");
        await ReportAsync(w, w.StaffA, "ไฟล์ report_final เปิดไม่ได้");
        await ReportAsync(w, w.StaffA, "ไฟล์ reportXfinal เปิดไม่ได้");

        Assert.Equal(["ส่วนลด 50% ไม่คิด"], (await w.Issues.QuickSearchAsync("0%", w.StaffA.Id)).Select(h => h.Title));
        Assert.Equal(["ไฟล์ report_final เปิดไม่ได้"], (await w.Issues.QuickSearchAsync("t_f", w.StaffA.Id)).Select(h => h.Title));
        Assert.Empty(await w.Issues.QuickSearchAsync("%%", w.StaffA.Id));
    }

    [Fact]
    public async Task An_issue_from_the_public_form_is_invisible_to_plain_staff_but_open_to_IT()
    {
        var w = await TestWorld.CreateAsync();
        await w.Issues.CreatePublicAsync(new PublicIssueRequest("สมหญิง ใจดี", "somying@example.com", null, null,
            "ลืมรหัสผ่านแล้วรีเซ็ตไม่ได้", "ใส่อีเมลแล้วไม่มีอะไรส่งมาให้เลยสักฉบับ", IssueCategory.Bug, IssueModule.Login, IssueImpact.Blocking), NoFiles, null);

        Assert.Empty(await w.Issues.QuickSearchAsync("รหัสผ่าน", w.StaffA.Id));
        Assert.Single(await w.Issues.QuickSearchAsync("รหัสผ่าน", w.ItA.Id));
        Assert.Single(await w.Issues.QuickSearchAsync("สมหญิง", w.ItA.Id));   // by the reporter's name
    }

    [Fact]
    public async Task Open_issues_come_first_and_the_list_is_short()
    {
        var w = await TestWorld.CreateAsync();
        var done = await ReportAsync(w, w.StaffA, "ปัญหาอีเมล ข้อที่แก้แล้ว");
        await w.Issues.StartAsync(done.Id, w.ItA.Id);
        await w.Issues.ResolveAsync(done.Id, new ResolveIssueRequest("แก้ไขแล้ว", IssueRootCause.CodeBug), w.ItA.Id);
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 10; i++) await ReportAsync(w, w.StaffA, $"ปัญหาอีเมล ข้อที่ {i}");

        var hits = await w.Issues.QuickSearchAsync("ปัญหาอีเมล", w.StaffA.Id);

        Assert.Equal(8, hits.Count);
        Assert.DoesNotContain(hits, h => h.Id == done.Id);
    }

    [Fact]
    public async Task People_are_offered_only_to_someone_who_may_open_the_user_list()
    {
        var w = await TestWorld.CreateAsync();

        Assert.Empty((await w.Search.SearchAsync("tester", w.ItA.Id)).Users);
        Assert.Empty((await w.Search.SearchAsync("tester", w.StaffA.Id)).Users);
        Assert.Empty((await w.Search.SearchAsync("tester", w.ViewerA.Id)).Users);

        var director = (await w.Search.SearchAsync("tester", w.Director.Id)).Users;
        Assert.Equal(5, director.Count);   // capped at five
        var exact = Assert.Single((await w.Search.SearchAsync("staff-b", w.Director.Id)).Users);
        Assert.Equal(w.StaffB.Id, exact.Id);
        Assert.Equal(w.StaffB.Email, exact.Email);
    }

    [Fact]
    public async Task A_manager_finds_people_in_their_own_department_only()
    {
        var w = await TestWorld.CreateAsync();

        var found = (await w.Search.SearchAsync("tester", w.ManagerA.Id)).Users;

        Assert.NotEmpty(found);
        Assert.DoesNotContain(found, u => u.Id == w.StaffB.Id || u.Id == w.ItA.Id);
        Assert.Empty((await w.Search.SearchAsync("staff-b", w.ManagerA.Id)).Users);
        Assert.Single((await w.Search.SearchAsync("staff-a", w.ManagerA.Id)).Users);
    }

    [Fact]
    public async Task A_deactivated_person_is_not_offered()
    {
        var w = await TestWorld.CreateAsync();
        w.StaffB.IsActive = false;
        await w.Db.SaveChangesAsync();

        Assert.Empty((await w.Search.SearchAsync("staff-b", w.Director.Id)).Users);
    }
}
