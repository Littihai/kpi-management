using KPI.Application.Issues;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Jobs;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class NotificationTests
{
    private static CreateIssueRequest Report(string title = "กดอนุมัติ KPI แล้วไม่มีอะไรเกิดขึ้น") =>
        new(title, "กดปุ่ม Approve แล้วสถานะยังเป็น Pending ทั้งที่ควรเป็น Active", IssueCategory.Bug, IssueModule.Application, IssueImpact.Blocking);

    private static readonly IReadOnlyList<IssueUpload> NoFiles = [];

    private static Task<IssueDetailDto> ReportAsync(TestWorld w, User by) => w.Issues.CreateAsync(Report(), NoFiles, by.Id, null);

    private static Task<List<Notification>> RowsAsync(TestWorld w) => w.Db.Notifications.AsNoTracking().ToListAsync();

    [Fact]
    public async Task A_new_report_is_kept_for_everyone_who_works_the_queue_but_not_for_the_reporter()
    {
        var w = await TestWorld.CreateAsync();

        var issue = await ReportAsync(w, w.ItA);   // IT reports it themselves: they are the actor, so they are left out

        var rows = await RowsAsync(w);
        Assert.Equivalent(new[] { w.ItB.Id, w.SuperAdmin.Id }, rows.Select(r => r.UserId).ToArray());
        Assert.All(rows, r =>
        {
            Assert.Equal(issue.Id, r.IssueId);
            Assert.Equal("created", r.Kind);
            Assert.Null(r.ReadAt);
        });
    }

    [Fact]
    public async Task A_member_of_staff_who_reports_is_not_notified_about_their_own_report()
    {
        var w = await TestWorld.CreateAsync();

        await ReportAsync(w, w.StaffA);

        var rows = await RowsAsync(w);
        Assert.DoesNotContain(rows, r => r.UserId == w.StaffA.Id);
        Assert.Equivalent(new[] { w.ItA.Id, w.ItB.Id, w.SuperAdmin.Id }, rows.Select(r => r.UserId).ToArray());
    }

    [Fact]
    public async Task A_deactivated_member_of_IT_is_not_notified()
    {
        var w = await TestWorld.CreateAsync();
        w.ItB.IsActive = false;
        await w.Db.SaveChangesAsync();

        await ReportAsync(w, w.StaffA);

        Assert.DoesNotContain(await RowsAsync(w), r => r.UserId == w.ItB.Id);
    }

    [Fact]
    public async Task A_staff_reply_is_kept_once_for_the_reporter()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        w.Db.Notifications.RemoveRange(w.Db.Notifications);
        await w.Db.SaveChangesAsync();

        await w.Issues.CommentAsync(issue.Id, "ลองล้างแคชแล้วเข้าใหม่อีกครั้ง", false, w.ItA.Id);

        var row = Assert.Single(await RowsAsync(w));
        Assert.Equal(w.StaffA.Id, row.UserId);
        Assert.Equal("IT ตอบกลับแล้ว", row.Title);
        Assert.Contains("ลองล้างแคช", row.Message);
        Assert.Equal(issue.Id, row.IssueId);
    }

    [Fact]
    public async Task An_internal_note_is_kept_for_nobody()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        w.Db.Notifications.RemoveRange(w.Db.Notifications);
        await w.Db.SaveChangesAsync();

        await w.Issues.CommentAsync(issue.Id, "ลูกค้ารายนี้ขอข้อมูลบ่อย ระวังคำพูด", true, w.ItA.Id);

        Assert.Empty(await RowsAsync(w));
        Assert.Equal(0, await w.Notifications.UnreadCountAsync(w.StaffA.Id));
    }

    [Fact]
    public async Task A_public_submission_is_kept_for_IT_only()
    {
        var w = await TestWorld.CreateAsync();

        await w.Issues.CreatePublicAsync(new PublicIssueRequest("สมหญิง ใจดี", "somying@example.com", null, w.DeptA.Id,
            "เข้าสู่ระบบไม่ได้เลย", "ใส่รหัสผ่านถูกแล้วแต่ขึ้นว่าไม่ถูกต้อง", IssueCategory.Bug, IssueModule.Login, IssueImpact.Blocking), NoFiles, "203.0.113.9");

        var rows = await RowsAsync(w);
        Assert.Equivalent(new[] { w.ItA.Id, w.ItB.Id, w.SuperAdmin.Id }, rows.Select(r => r.UserId).ToArray());
    }

    [Fact]
    public async Task A_down_hub_does_not_fail_the_change_and_the_entries_are_still_kept()
    {
        var w = await TestWorld.CreateAsync();
        w.Notifier.Throw = true;

        var issue = await ReportAsync(w, w.StaffA);

        Assert.NotNull(await w.Db.Issues.FindAsync(issue.Id));
        Assert.NotEmpty(await RowsAsync(w));
    }

    /// <summary>Refuses to save a notification, as a full disk or a deadlock would.</summary>
    private sealed class RefusesNotifications(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<Notification>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("notifications table unavailable");
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    [Fact]
    public async Task A_failed_save_of_the_entries_never_fails_the_change_and_leaves_nothing_pending()
    {
        var w = await TestWorld.CreateAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Email:PacingMs"] = "0" }).Build();
        await using var db = new RefusesNotifications(w.Options);
        var publisher = new IssueEventPublisher(w.Notifier, db, w.Email, config, w.Settings, NullLogger<IssueEventPublisher>.Instance);
        var issues = new IssueService(db, new UserRepository(db), publisher, w.Settings, w.Clock);

        var created = await issues.CreateAsync(Report(), NoFiles, w.StaffA.Id, null);
        await issues.CommentAsync(created.Id, "ลองใหม่อีกครั้งนะครับ", false, w.ItA.Id);   // a later save must not trip over the dropped rows

        Assert.NotNull(await w.Db.Issues.FindAsync(created.Id));
        Assert.Empty(await RowsAsync(w));
        Assert.Empty(db.ChangeTracker.Entries<Notification>());
        Assert.Contains(w.Notifier.Notifications, n => n.Notification.IssueId == created.Id);   // the toast still went out
    }

    [Fact]
    public async Task Entries_are_listed_newest_first_and_can_be_marked_read_one_by_one_or_all_at_once()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await w.Issues.CommentAsync(issue.Id, "กำลังตรวจสอบสิทธิ์ของปุ่มอยู่ครับ", false, w.ItA.Id);

        var all = await w.Notifications.ListAsync(new NotificationQuery(), w.StaffA.Id);
        Assert.Equal(2, all.Total);
        Assert.Equal(2, await w.Notifications.UnreadCountAsync(w.StaffA.Id));

        await w.Notifications.MarkReadAsync(all.Items[0].Id, w.StaffA.Id);
        Assert.Equal(1, await w.Notifications.UnreadCountAsync(w.StaffA.Id));
        Assert.Single((await w.Notifications.ListAsync(new NotificationQuery(UnreadOnly: true), w.StaffA.Id)).Items);

        Assert.Equal(1, await w.Notifications.MarkAllReadAsync(w.StaffA.Id));
        Assert.Equal(0, await w.Notifications.UnreadCountAsync(w.StaffA.Id));
        Assert.Equal(2, (await w.Notifications.ListAsync(new NotificationQuery(), w.StaffA.Id)).Total);   // read entries stay listed
    }

    [Fact]
    public async Task Opening_an_issue_marks_only_that_issues_entries_read()
    {
        var w = await TestWorld.CreateAsync();
        var first = await ReportAsync(w, w.StaffA);
        var second = await ReportAsync(w, w.StaffA);
        await w.Issues.CommentAsync(first.Id, "รับเรื่องแล้วครับ", false, w.ItA.Id);
        await w.Issues.CommentAsync(second.Id, "รับเรื่องแล้วครับ", false, w.ItA.Id);

        Assert.Equal(1, await w.Notifications.MarkIssueReadAsync(first.Id, w.StaffA.Id));

        var unread = (await w.Notifications.ListAsync(new NotificationQuery(UnreadOnly: true), w.StaffA.Id)).Items;
        Assert.Equal(second.Id, Assert.Single(unread).IssueId);
        Assert.Equal(0, await w.Notifications.MarkIssueReadAsync(first.Id, w.StaffA.Id));
    }

    [Fact]
    public async Task Someone_elses_entry_is_not_found_and_stays_unread()
    {
        var w = await TestWorld.CreateAsync();
        var issue = await ReportAsync(w, w.StaffA);
        var itRow = await w.Db.Notifications.FirstAsync(n => n.UserId == w.ItA.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => w.Notifications.MarkReadAsync(itRow.Id, w.StaffA.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => w.Notifications.MarkReadAsync(Guid.NewGuid(), w.StaffA.Id));
        Assert.Null((await w.Db.Notifications.AsNoTracking().FirstAsync(n => n.Id == itRow.Id)).ReadAt);

        // Marking an issue read for someone who has no entries about it touches nobody else's.
        Assert.Equal(0, await w.Notifications.MarkIssueReadAsync(issue.Id, w.StaffA.Id));
        Assert.Equal(1, await w.Notifications.UnreadCountAsync(w.ItA.Id));
    }

    [Fact]
    public async Task The_unread_count_stops_at_one_hundred()
    {
        var w = await TestWorld.CreateAsync();
        for (var i = 0; i < 105; i++)
            w.Db.Notifications.Add(new Notification { UserId = w.StaffA.Id, Kind = "comment", Title = "t", Message = "m", CreatedAt = w.Clock.UtcNow });
        await w.Db.SaveChangesAsync();

        Assert.Equal(INotificationService.UnreadCap, await w.Notifications.UnreadCountAsync(w.StaffA.Id));
        Assert.Equal(105, (await w.Notifications.ListAsync(new NotificationQuery(PageSize: 100), w.StaffA.Id)).Total);
    }

    [Fact]
    public async Task Entries_about_an_issue_outside_the_persons_scope_are_hidden_from_the_list_and_the_count()
    {
        var w = await TestWorld.CreateAsync();
        var elsewhere = await ReportAsync(w, w.StaffB);          // department B: StaffA (own scope) cannot open it
        var mine = await ReportAsync(w, w.StaffA);
        foreach (var issueId in new[] { elsewhere.Id, mine.Id })
            w.Db.Notifications.Add(new Notification { UserId = w.StaffA.Id, IssueId = issueId, Kind = "comment", Title = "t", Message = "m" });
        w.Db.Notifications.Add(new Notification { UserId = w.StaffA.Id, IssueId = null, Kind = "other", Title = "t", Message = "m" });
        await w.Db.SaveChangesAsync();

        var list = await w.Notifications.ListAsync(new NotificationQuery(), w.StaffA.Id);

        Assert.DoesNotContain(list.Items, n => n.IssueId == elsewhere.Id);
        Assert.Equal(2, list.Total);
        Assert.Equal(2, await w.Notifications.UnreadCountAsync(w.StaffA.Id));
    }

    [Fact]
    public async Task Without_the_right_to_view_issues_there_is_no_notification_centre()
    {
        var w = await TestWorld.CreateAsync();
        var role = new Role { Name = "NoIssues", IssueVisibility = IssueVisibility.Own };
        w.Db.Roles.Add(role);
        await w.Db.SaveChangesAsync();
        var user = new User { FirstName = "no", LastName = "issues", Email = "noissues@example.com", PasswordHash = "x", RoleId = role.Id, IsActive = true };
        w.Db.Users.Add(user);
        await w.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => w.Notifications.UnreadCountAsync(user.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Notifications.ListAsync(new NotificationQuery(), Guid.NewGuid()));
    }

    [Fact]
    public async Task The_daily_job_removes_read_entries_after_the_retention_and_unread_ones_after_three_times_as_long()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.NotificationDays, "30");
        var now = w.Clock.UtcNow;
        Notification Row(int ageDays, bool read) => new()
        {
            UserId = w.StaffA.Id, Kind = "comment", Title = $"{(read ? "read" : "unread")}-{ageDays}", Message = "m",
            CreatedAt = now.AddDays(-ageDays), ReadAt = read ? now.AddDays(-ageDays + 1) : null,
        };
        w.Db.Notifications.AddRange(Row(31, read: true), Row(29, read: true), Row(31, read: false), Row(89, read: false), Row(91, read: false));
        await w.Db.SaveChangesAsync();

        await new MaintenanceJob(w.Issues, w.Notifications, w.Db, w.Clock, w.Settings, w.AuthLog, NullLogger<MaintenanceJob>.Instance).RunAsync();

        Assert.Equivalent(new[] { "read-29", "unread-31", "unread-89" }, (await RowsAsync(w)).Select(r => r.Title).ToArray());
    }

    [Fact]
    public void The_retention_cannot_be_set_below_a_week()
    {
        var definition = SettingCatalog.All.Single(d => d.Key == SettingCatalog.Keys.NotificationDays);
        Assert.Equal("60", definition.Default);
        Assert.Equal(7, definition.Min);
        Assert.Equal(730, definition.Max);
    }
}
