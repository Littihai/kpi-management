using KPI.API.Security;
using KPI.Application.Issues;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using Microsoft.Extensions.Options;

namespace KPI.Tests;

/// <summary>The whole-form ceiling on reports and attachment bytes that anyone can send without signing in.</summary>
public class PublicIntakeBudgetTests
{
    private static PublicIssueRequest PublicReport(string title = "เข้าสู่ระบบไม่ได้เลย") =>
        new("สมหญิง ใจดี", "somying@example.com", null, null, title, "ใส่รหัสผ่านถูกแล้วแต่ขึ้นว่าไม่ถูกต้อง",
            IssueCategory.Bug, IssueModule.Login, IssueImpact.Blocking);

    private static IssueUpload Picture(int kilobytes)
    {
        var data = new byte[kilobytes * 1024];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0); // PNG signature
        return new IssueUpload("screen.png", "image/png", data);
    }

    private static PublicIntakeBudget BudgetFor(TestWorld w, int perDay = 300, int megabytesPerDay = 300, PublicIntakeLedger? ledger = null) =>
        new(w.Db, ledger ?? new PublicIntakeLedger(),
            Options.Create(new PublicSubmitOptions { MaxPerDay = perDay, MaxUploadMbPerDay = megabytesPerDay }), w.Clock);

    /// <summary>Asks for room and gives it straight back, like a report that was saved in between.</summary>
    private static async Task EnsureRoomAsync(PublicIntakeBudget budget, long bytes)
    {
        using var _ = await budget.ReserveAsync(bytes);
    }

    [Fact]
    public async Task There_is_room_while_the_form_is_quiet()
    {
        var w = await TestWorld.CreateAsync();
        await EnsureRoomAsync(BudgetFor(w, perDay: 3), 100 * 1024);
    }

    [Fact]
    public async Task Once_the_day_is_full_of_reports_nobody_gets_in_whatever_their_address()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, perDay: 3);
        for (var i = 0; i < 3; i++) await w.Issues.CreatePublicAsync(PublicReport(), [], $"203.0.113.{i + 1}");

        var full = await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(budget, 0));

        Assert.InRange(full.RetryAfter, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task Room_comes_back_when_the_oldest_report_is_more_than_a_day_old()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, perDay: 2);
        await w.Issues.CreatePublicAsync(PublicReport(), [], null);
        w.Clock.Advance(TimeSpan.FromHours(23));
        await w.Issues.CreatePublicAsync(PublicReport(), [], null);
        var full = await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(budget, 0));
        Assert.InRange(full.RetryAfter, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61)); // the first report ages out in 1 hour

        w.Clock.Advance(TimeSpan.FromHours(2)); // the first report is now 25 hours old

        await EnsureRoomAsync(budget, 0);
    }

    [Fact]
    public async Task Attachments_are_added_up_across_reports_and_addresses()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, megabytesPerDay: 2);
        await w.Issues.CreatePublicAsync(PublicReport(), [Picture(400)], "203.0.113.1");
        await w.Issues.CreatePublicAsync(PublicReport(), [Picture(400)], "203.0.113.2");
        await w.Issues.CreatePublicAsync(PublicReport(), [Picture(100), Picture(100)], "203.0.113.3"); // 1.0 MB in total

        await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(budget, 1100 * 1024)); // 2.1 MB
        await EnsureRoomAsync(budget, 900 * 1024);                                                     // 1.9 MB fits
    }

    [Fact]
    public async Task A_report_without_attachments_is_let_in_even_when_the_size_cap_is_already_used_up()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, megabytesPerDay: 1);
        await w.Issues.CreatePublicAsync(PublicReport(), [Picture(600)], null);
        await w.Issues.CreatePublicAsync(PublicReport(), [Picture(600)], null); // 1.2 MB stored, over the 1 MB cap

        await EnsureRoomAsync(budget, 0);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(budget, 1));
    }

    [Fact]
    public async Task Reports_from_signed_in_people_do_not_use_up_the_public_budget()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, perDay: 1, megabytesPerDay: 1);
        await w.Issues.CreateAsync(
            new CreateIssueRequest("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง",
                IssueCategory.Bug, IssueModule.Application, IssueImpact.Degraded),
            [Picture(900)], w.StaffA.Id, "10.1.2.3");

        await EnsureRoomAsync(budget, 900 * 1024);
    }

    [Fact]
    public async Task A_limit_of_zero_switches_each_check_off()
    {
        var w = await TestWorld.CreateAsync();
        var noLimits = BudgetFor(w, perDay: 0, megabytesPerDay: 0);
        for (var i = 0; i < 3; i++) await w.Issues.CreatePublicAsync(PublicReport(), [Picture(600)], null);
        await EnsureRoomAsync(noLimits, 50 * 1024 * 1024);

        // Each switch is separate: reports unlimited, bytes still capped.
        var onlyBytes = BudgetFor(w, perDay: 0, megabytesPerDay: 1);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(onlyBytes, 1));
    }

    [Fact]
    public async Task The_shipped_defaults_are_300_reports_and_300_MB_a_day()
    {
        var defaults = new PublicSubmitOptions();
        Assert.Equal(300, defaults.MaxPerDay);
        Assert.Equal(300, defaults.MaxUploadMbPerDay);

        var w = await TestWorld.CreateAsync();
        var budget = new PublicIntakeBudget(w.Db, new PublicIntakeLedger(), Options.Create(defaults), w.Clock);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => EnsureRoomAsync(budget, 301L * 1024 * 1024));
        await EnsureRoomAsync(budget, 300L * 1024 * 1024);
    }

    // ───────────── reports arriving together ─────────────

    [Fact]
    public async Task A_report_being_saved_right_now_already_counts_so_a_burst_cannot_all_slip_under_the_ceiling()
    {
        var w = await TestWorld.CreateAsync();
        var ledger = new PublicIntakeLedger();
        var budget = BudgetFor(w, perDay: 1, ledger: ledger);

        using var first = await budget.ReserveAsync(0); // nothing is stored yet: the database still says "empty"

        await Assert.ThrowsAsync<TooManyRequestsException>(() => budget.ReserveAsync(0));
    }

    [Fact]
    public async Task Bytes_in_flight_count_against_the_size_cap_and_are_given_back_when_the_report_is_done()
    {
        var w = await TestWorld.CreateAsync();
        var ledger = new PublicIntakeLedger();
        var budget = BudgetFor(w, megabytesPerDay: 1, ledger: ledger);

        var first = await budget.ReserveAsync(700 * 1024);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => budget.ReserveAsync(700 * 1024));

        first.Dispose();
        first.Dispose(); // giving it back twice must not hand out extra room
        using var second = await budget.ReserveAsync(700 * 1024);
        await Assert.ThrowsAsync<TooManyRequestsException>(() => budget.ReserveAsync(700 * 1024));
    }

    [Fact]
    public async Task A_report_that_fails_to_save_gives_its_room_back()
    {
        var w = await TestWorld.CreateAsync();
        var budget = BudgetFor(w, perDay: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var room = await budget.ReserveAsync(0);
            throw new InvalidOperationException("could not save");
        });

        await EnsureRoomAsync(budget, 0);
    }

    [Fact]
    public async Task Parallel_requests_get_exactly_the_room_that_exists()
    {
        // Twenty requests ask at once for one of 5 places; the ledger's gate lets them through the check one at a time.
        var w = await TestWorld.CreateAsync();
        var ledger = new PublicIntakeLedger();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try { return await BudgetFor(w, perDay: 5, ledger: ledger).ReserveAsync(0); }
            catch (TooManyRequestsException) { return null; }
        }));

        Assert.Equal(5, results.Count(r => r is not null));
    }
}
