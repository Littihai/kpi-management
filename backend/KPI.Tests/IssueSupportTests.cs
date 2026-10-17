using KPI.Application.Issues;
using KPI.Application.Notifications;
using KPI.Domain.Entities;
using KPI.Domain.Issues;
using KPI.Infrastructure.Jobs;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class IssueEmailTemplateTests
{
    private static IssueEmailMessage Message(IssueEmailKind kind, string title = "เข้าระบบไม่ได้", string? detail = null,
        string? actor = "สมชาย", string toName = "สมหญิง") =>
        new(kind, "x@example.com", toName, Guid.NewGuid(), "ISS-2569-0007", title, actor, detail,
            DateTime.UtcNow.AddHours(2));

    [Fact]
    public void Every_kind_names_the_issue_number_and_title_in_the_subject()
    {
        foreach (var kind in Enum.GetValues<IssueEmailKind>())
        {
            var (subject, html) = IssueEmailTemplates.Build(Message(kind, detail: "รายละเอียด"), null);
            Assert.Contains("ISS-2569-0007", subject);
            Assert.Contains("เข้าระบบไม่ได้", subject);
            Assert.Contains("เข้าระบบไม่ได้", html);
            Assert.Contains("สวัสดีคุณ สมหญิง", html);
        }
    }

    [Fact]
    public void Everything_a_person_typed_is_html_encoded()
    {
        var evil = "<script>alert(1)</script>";
        foreach (var kind in Enum.GetValues<IssueEmailKind>())
        {
            var (_, html) = IssueEmailTemplates.Build(Message(kind, title: evil, detail: evil, actor: evil, toName: evil), null);
            Assert.DoesNotContain("<script>", html);
        }
    }

    [Fact]
    public void The_received_mail_states_the_response_target_and_the_resolved_mail_asks_for_confirmation()
    {
        var received = IssueEmailTemplates.Build(Message(IssueEmailKind.Received), null).Html;
        var resolved = IssueEmailTemplates.Build(Message(IssueEmailKind.Resolved, detail: "ปรับสิทธิ์แล้ว"), null).Html;

        Assert.Contains("ชั่วโมง", received);
        Assert.Contains("ปรับสิทธิ์แล้ว", resolved);
        Assert.Contains("ยืนยัน", resolved);
        Assert.Contains("3 วัน", resolved);
    }

    [Fact]
    public void The_button_links_to_the_issue_only_when_a_base_url_is_configured()
    {
        var message = Message(IssueEmailKind.StaffReplied, detail: "รับเรื่องแล้ว");

        Assert.DoesNotContain("<a href", IssueEmailTemplates.Build(message, null).Html);
        Assert.Contains($"https://kpi.example.com/issues/{message.IssueId}",
            IssueEmailTemplates.Build(message, "https://kpi.example.com/").Html);
    }

    [Theory]
    [InlineData(10, "นาที")]
    [InlineData(120, "ชั่วโมง")]
    [InlineData(60 * 24 * 5, "วัน")]
    public void Deadlines_are_described_in_plain_thai(int minutesAhead, string unit)
    {
        Assert.Contains(unit, IssueEmailTemplates.FormatDue(DateTime.UtcNow.AddMinutes(minutesAhead).AddSeconds(30)));
        Assert.Equal("ทันที", IssueEmailTemplates.FormatDue(DateTime.UtcNow.AddMinutes(-1)));
    }
}

public class MaintenanceJobTests
{
    private static MaintenanceJob JobFor(TestWorld w, int retentionDays = 30) =>
        new(w.Issues, w.Db, w.Clock,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Audit:RequestLogRetentionDays"] = retentionDays.ToString(),
            }).Build(),
            NullLogger<MaintenanceJob>.Instance);

    [Fact]
    public async Task The_daily_job_closes_ignored_fixes_and_removes_old_request_logs_but_keeps_audit_history()
    {
        var w = await TestWorld.CreateAsync();
        var created = await w.Issues.CreateAsync(
            new CreateIssueRequest("ปุ่มไม่ทำงาน", "กดแล้วไม่มีอะไรเกิดขึ้นเลย", IssueCategory.Bug, IssueModule.Kpi, IssueImpact.Minor),
            [], w.StaffA.Id, null);
        await w.Issues.StartAsync(created.Id, w.ItA.Id);
        await w.Issues.ResolveAsync(created.Id, new ResolveIssueRequest("แก้แล้ว ลองอีกครั้ง", IssueRootCause.CodeBug), w.ItA.Id);

        w.Db.RequestLogs.AddRange(
            new RequestLog { Method = "POST", Path = "/old", StatusCode = 200, CreatedAt = w.Clock.UtcNow.AddDays(-45) },
            new RequestLog { Method = "POST", Path = "/recent", StatusCode = 200, CreatedAt = w.Clock.UtcNow.AddDays(-2) });
        await w.Db.SaveChangesAsync();
        var auditBefore = await w.Db.AuditLogs.CountAsync();

        w.Clock.Advance(TimeSpan.FromDays(4));
        await JobFor(w).RunAsync();

        Assert.Equal(IssueStatus.Closed, (await w.Issues.GetAsync(created.Id, w.ItA.Id)).Status);
        Assert.Equal(["/recent"], await w.Db.RequestLogs.Select(r => r.Path).ToListAsync());
        Assert.True(await w.Db.AuditLogs.CountAsync() >= auditBefore);        // history is never purged
    }

    [Fact]
    public async Task Retention_cannot_be_set_below_a_week()
    {
        var w = await TestWorld.CreateAsync();
        w.Db.RequestLogs.Add(new RequestLog { Method = "POST", Path = "/three-days", StatusCode = 200, CreatedAt = w.Clock.UtcNow.AddDays(-3) });
        await w.Db.SaveChangesAsync();

        await JobFor(w, retentionDays: 1).RunAsync();

        Assert.Single(await w.Db.RequestLogs.ToListAsync());
    }
}
