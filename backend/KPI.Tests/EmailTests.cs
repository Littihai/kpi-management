using KPI.Application.Kpis;
using KPI.Application.Notifications;
using KPI.Domain.Enums;
using KPI.Infrastructure.Services;

namespace KPI.Tests;

public class KpiEmailTemplateTests
{
    private static KpiEmailMessage Message(KpiEmailKind kind, string name = "Cut waste", string toName = "Ann",
        string? actor = "Bob", string? reason = null) =>
        new(kind, "ann@example.com", toName, Guid.NewGuid(), name, "Finance", "Carl", actor,
            40, 100, new DateTime(2026, 12, 31), reason);

    [Fact]
    public void Every_kind_has_a_subject_naming_the_kpi()
    {
        foreach (var kind in Enum.GetValues<KpiEmailKind>())
        {
            var (subject, html) = KpiEmailTemplates.Build(Message(kind), null);
            Assert.Contains("Cut waste", subject);
            Assert.Contains("Cut waste", html);
            Assert.Contains("Hi Ann,", html);
        }
    }

    [Fact]
    public void User_supplied_text_is_html_encoded()
    {
        var evil = "<script>alert(1)</script>";
        var (_, html) = KpiEmailTemplates.Build(
            Message(KpiEmailKind.Rejected, name: evil, toName: evil, actor: evil, reason: evil), null);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void A_subject_is_always_a_single_line()
    {
        Assert.Equal("a b c", KpiEmailTemplates.SingleLine("a\r\nb\nc"));
    }

    [Fact]
    public void The_open_link_appears_only_when_a_base_url_is_configured()
    {
        var message = Message(KpiEmailKind.Approved);

        var (_, without) = KpiEmailTemplates.Build(message, null);
        var (_, with) = KpiEmailTemplates.Build(message, "https://kpi.example.com/");

        Assert.DoesNotContain("Open KPI", without);
        Assert.Contains($"https://kpi.example.com/kpi/{message.KpiId}", with);
    }

    [Fact]
    public void Rejection_shows_the_reason_and_status_alerts_show_progress()
    {
        var rejected = KpiEmailTemplates.Build(Message(KpiEmailKind.Rejected, reason: "Target too low"), null).Html;
        var atRisk = KpiEmailTemplates.Build(Message(KpiEmailKind.AtRisk), null).Html;

        Assert.Contains("Target too low", rejected);
        Assert.Contains("Progress 40 of 100", atRisk);
        Assert.Contains("31 Dec 2026", atRisk);
    }
}

public class KpiEmailFlowTests
{
    private static CreateKpiRequest NewRequest(Guid departmentId) =>
        new("Reduce downtime", "d", 2026, 100, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), departmentId);

    [Fact]
    public async Task Creating_a_kpi_tells_the_other_managers_of_that_department_only()
    {
        var w = await TestWorld.CreateAsync();
        var secondManager = await w.AddUserAsync("manager-a2", "Manager", w.DeptA.Id);

        await w.Service.CreateAsync(NewRequest(w.DeptA.Id), w.ManagerA.Id);

        // the creator is not told about their own action; the other manager in the department is
        Assert.Equal([secondManager.Email], w.Email.To(KpiEmailKind.Created));
    }

    [Fact]
    public async Task A_SuperAdmin_creating_in_a_department_tells_its_managers()
    {
        var w = await TestWorld.CreateAsync();

        await w.Service.CreateAsync(NewRequest(w.DeptA.Id), w.SuperAdmin.Id);

        Assert.Equal([w.ManagerA.Email], w.Email.To(KpiEmailKind.Created));
    }

    [Fact]
    public async Task Submitting_asks_the_directors_to_approve()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        var sent = Assert.Single(w.Email.Sent);
        Assert.Equal(KpiEmailKind.Submitted, sent.Kind);
        Assert.Equal(w.Director.Email, sent.ToEmail);
        Assert.Equal(kpi.Name, sent.KpiName);
        Assert.Contains("manager-a", sent.ActorName);
    }

    [Fact]
    public async Task With_no_active_director_the_SuperAdmin_is_asked_instead()
    {
        var w = await TestWorld.CreateAsync();
        w.Director.IsActive = false;
        await w.Db.SaveChangesAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Equal([w.SuperAdmin.Email], w.Email.To(KpiEmailKind.Submitted));
    }

    [Fact]
    public async Task Approval_and_rejection_go_to_the_owner_only()
    {
        var w = await TestWorld.CreateAsync();
        var approved = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);
        var rejected = await w.AddKpiAsync(KpiStatus.PendingApproval, w.ManagerA);

        await w.Service.ApproveAsync(approved.Id, w.Director.Id);
        await w.Service.RejectAsync(rejected.Id, "Target too low", w.Director.Id);

        Assert.Equal([w.ManagerA.Email], w.Email.To(KpiEmailKind.Approved));
        var rejection = Assert.Single(w.Email.Sent, m => m.Kind == KpiEmailKind.Rejected);
        Assert.Equal(w.ManagerA.Email, rejection.ToEmail);
        Assert.Equal("Target too low", rejection.Reason);
    }

    [Fact]
    public async Task Falling_behind_emails_the_owner_and_the_manager_once_even_if_they_are_the_same_person()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA,
            start: DateTime.UtcNow.AddDays(-60), due: DateTime.UtcNow.AddDays(40));

        // a team leader reports low progress on a KPI owned by the department manager
        await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 10, ""), w.TeamLeaderA.Id);

        Assert.Equal([w.ManagerA.Email], w.Email.To(KpiEmailKind.AtRisk));
    }

    [Fact]
    public async Task Completion_tells_managers_and_directors_but_not_the_person_who_completed_it()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);

        await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 100, ""), w.ManagerA.Id);

        Assert.Equal([w.Director.Email], w.Email.To(KpiEmailKind.Completed)); // ManagerA did it
    }

    [Fact]
    public async Task Progress_that_changes_nothing_sends_nothing()
    {
        var w = await TestWorld.CreateAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Active, w.ManagerA);

        await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 5, ""), w.ManagerA.Id);
        await w.Service.UpdateProgressAsync(new UpdateKpiProgressRequest(kpi.Id, 8, ""), w.ManagerA.Id);

        Assert.Empty(w.Email.Sent);
    }

    [Fact]
    public async Task Deactivated_people_are_never_emailed()
    {
        var w = await TestWorld.CreateAsync();
        w.Director.IsActive = false;
        w.SuperAdmin.IsActive = false;
        await w.Db.SaveChangesAsync();
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Empty(w.Email.Sent);
    }

    [Fact]
    public async Task A_broken_mail_provider_never_fails_the_operation()
    {
        var w = await TestWorld.CreateAsync();
        w.Email.Throw = true;
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        var dto = await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Equal(KpiStatus.PendingApproval, dto.Status);
        w.Clear();
        Assert.Equal(KpiStatus.PendingApproval, (await w.Db.Kpis.FindAsync(kpi.Id))!.Status);
    }

    [Fact]
    public async Task E_mail_can_be_switched_off_by_configuration()
    {
        var w = await TestWorld.CreateAsync(emailEnabled: false);
        var kpi = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);

        await w.Service.SubmitForApprovalAsync(kpi.Id, w.ManagerA.Id);

        Assert.Empty(w.Email.Sent);
        Assert.NotEmpty(w.Notifier.Notifications); // the live push is unaffected
    }

    [Fact]
    public async Task Edits_closing_and_deleting_do_not_send_e_mail()
    {
        var w = await TestWorld.CreateAsync();
        var draft = await w.AddKpiAsync(KpiStatus.Draft, w.ManagerA);
        var completed = await w.AddKpiAsync(KpiStatus.Completed, w.ManagerA, progress: 100);

        await w.Service.UpdateAsync(draft.Id,
            new UpdateKpiRequest("New name", "d", 2026, 100, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), w.DeptA.Id),
            w.ManagerA.Id);
        await w.Service.CloseAsync(completed.Id, w.Director.Id);
        await w.Service.DeleteAsync(draft.Id, w.SuperAdmin.Id);

        Assert.Empty(w.Email.Sent);
    }
}
