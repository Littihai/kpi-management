using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Admin;
using KPI.Application.Auth;
using KPI.Infrastructure.Repositories;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class PasswordPolicyTests
{
    private static async Task<string> MessageAsync(DefaultSettings settings, string? password)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => new PasswordPolicyService(settings).EnsureAsync(password));
        return ex.Message;
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("alllowercase")]
    [InlineData("pass word 1")]
    public async Task By_default_only_the_length_matters(string password) =>
        await new PasswordPolicyService(new DefaultSettings()).EnsureAsync(password);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task A_password_shorter_than_the_minimum_is_refused(string? password)
    {
        var message = await MessageAsync(new DefaultSettings(), password);
        Assert.Contains("8", message);
    }

    [Fact]
    public async Task The_minimum_length_follows_the_setting()
    {
        var settings = new DefaultSettings().Set(SettingCatalog.Keys.PasswordMinLength, "12");
        Assert.Contains("12", await MessageAsync(settings, "elevenchars1"[..11]));
        await new PasswordPolicyService(settings).EnsureAsync("twelve-chars");
    }

    [Theory]
    [InlineData(SettingCatalog.Keys.PasswordRequireUpper, "alllower1!", "ตัวพิมพ์ใหญ่")]
    [InlineData(SettingCatalog.Keys.PasswordRequireLower, "ALLUPPER1!", "ตัวพิมพ์เล็ก")]
    [InlineData(SettingCatalog.Keys.PasswordRequireDigit, "NoDigits!!", "ตัวเลข")]
    [InlineData(SettingCatalog.Keys.PasswordRequireSpecial, "NoSpecial12", "อักขระพิเศษ")]
    public async Task Each_character_rule_is_enforced_and_named(string key, string failing, string expected)
    {
        var settings = new DefaultSettings().Set(key, "true");
        Assert.Contains(expected, await MessageAsync(settings, failing));
        await new PasswordPolicyService(settings).EnsureAsync("Good-Passw0rd!");
    }

    [Fact]
    public async Task Every_broken_rule_is_listed_at_once_so_the_person_fixes_it_in_one_go()
    {
        var settings = new DefaultSettings()
            .Set(SettingCatalog.Keys.PasswordRequireUpper, "true").Set(SettingCatalog.Keys.PasswordRequireDigit, "true")
            .Set(SettingCatalog.Keys.PasswordRequireSpecial, "true");

        var message = await MessageAsync(settings, "abc");

        Assert.Contains("8", message);
        Assert.Contains("ตัวพิมพ์ใหญ่", message);
        Assert.Contains("ตัวเลข", message);
        Assert.Contains("อักขระพิเศษ", message);
    }

    [Fact]
    public async Task The_policy_can_be_described_for_the_screen()
    {
        var settings = new DefaultSettings().Set(SettingCatalog.Keys.PasswordRequireDigit, "true").Set(SettingCatalog.Keys.PasswordMaxAgeDays, "90");

        var info = await new PasswordPolicyService(settings).DescribeAsync();

        Assert.Equal(new PasswordPolicyInfo(8, false, false, true, false, 0, 90), info);
    }

    [Fact]
    public void The_security_settings_have_safe_limits_and_the_old_behaviour_as_default()
    {
        var d = (string key) => SettingCatalog.Find(key)!;
        Assert.Equal("8", d(SettingCatalog.Keys.PasswordMinLength).Default);
        Assert.Equal("0", d(SettingCatalog.Keys.PasswordMaxAgeDays).Default);
        Assert.Equal("0", d(SettingCatalog.Keys.PasswordHistoryCount).Default);
        Assert.Equal("0", d(SettingCatalog.Keys.LoginMaxFailures).Default);   // lock-out is opt-in
        Assert.Equal("7", d(SettingCatalog.Keys.SessionDays).Default);        // the lifetime tokens had before
        Assert.Throws<RequestValidationException>(() => SettingCatalog.Normalize(d(SettingCatalog.Keys.PasswordMinLength), "3"));
        Assert.Throws<RequestValidationException>(() => SettingCatalog.Normalize(d(SettingCatalog.Keys.SessionDays), "400"));
    }
}

public class PasswordLifecycleTests
{
    private static async Task<(TestWorld World, User User)> CreateAsync()
    {
        var w = await TestWorld.CreateAsync();
        var user = await w.AddUserAsync("carol", RoleNames.Staff, w.DeptA.Id);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Original-1", 4);
        user.CreatedAt = w.Clock.UtcNow.AddDays(-30);                 // the test clock, not the machine clock
        await w.Db.SaveChangesAsync();
        return (w, user);
    }

    [Fact]
    public async Task Setting_a_password_hashes_it_dates_it_remembers_it_and_logs_it()
    {
        var (w, user) = await CreateAsync();

        await w.Passwords.SetAsync(user, "Another-Pass-2", PasswordSetKind.Self);

        Assert.True(BCrypt.Net.BCrypt.Verify("Another-Pass-2", user.PasswordHash));
        Assert.Equal(w.Clock.UtcNow, user.PasswordChangedAt);
        Assert.False(user.MustChangePassword);
        Assert.Equal(2, await w.Db.PasswordHistory.CountAsync(h => h.UserId == user.Id));   // the one it replaced, and the new one
        Assert.Contains(await w.Db.AuthLogs.ToListAsync(), l => l.Event == AuthEvents.PasswordChanged && l.UserId == user.Id && l.Success);
    }

    [Fact]
    public async Task A_policy_breach_changes_nothing()
    {
        var (w, user) = await CreateAsync();
        var before = user.PasswordHash;

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Passwords.SetAsync(user, "short", PasswordSetKind.Self));

        Assert.Equal(before, user.PasswordHash);
        Assert.Empty(await w.Db.PasswordHistory.ToListAsync());
        Assert.Empty(await w.Db.AuthLogs.ToListAsync());
    }

    [Fact]
    public async Task Recent_passwords_cannot_be_reused_but_older_ones_can()
    {
        var (w, user) = await CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.PasswordHistoryCount, "2");

        await w.Passwords.SetAsync(user, "First-Pass-1", PasswordSetKind.Self);
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        await w.Passwords.SetAsync(user, "Second-Pass-2", PasswordSetKind.Self);
        w.Clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<RequestValidationException>(() => w.Passwords.SetAsync(user, "Second-Pass-2", PasswordSetKind.Self)); // the current one
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Passwords.SetAsync(user, "First-Pass-1", PasswordSetKind.Self));  // the one before
        await w.Passwords.SetAsync(user, "Third-Pass-3", PasswordSetKind.Self);
        w.Clock.Advance(TimeSpan.FromMinutes(1));
        await w.Passwords.SetAsync(user, "First-Pass-1", PasswordSetKind.Self);    // now three back: allowed again
    }

    [Fact]
    public async Task An_account_from_before_the_history_still_cannot_reuse_its_current_password()
    {
        var (w, user) = await CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.PasswordHistoryCount, "3");

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => w.Passwords.SetAsync(user, "Original-1", PasswordSetKind.Self));
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public async Task With_the_history_off_the_same_password_is_accepted()
    {
        var (w, user) = await CreateAsync();
        await w.Passwords.SetAsync(user, "Original-1", PasswordSetKind.Self);
    }

    [Fact]
    public async Task Only_the_newest_twenty_four_passwords_are_kept()
    {
        var (w, user) = await CreateAsync();
        for (var i = 0; i < 27; i++)
        {
            w.Clock.Advance(TimeSpan.FromMinutes(1));
            await w.Passwords.SetAsync(user, $"Rotation-Pass-{i}", PasswordSetKind.Self);
        }

        Assert.Equal(24, await w.Db.PasswordHistory.CountAsync(h => h.UserId == user.Id));
    }

    [Fact]
    public async Task After_an_administrator_sets_a_password_the_person_must_choose_their_own_only_if_the_setting_says_so()
    {
        var (w, user) = await CreateAsync();

        await w.Passwords.SetAsync(user, "Admin-Set-1", PasswordSetKind.AdminSet);
        Assert.False(user.MustChangePassword);

        w.Settings.Set(SettingCatalog.Keys.ForceChangeOnAdminSet, "true");
        await w.Passwords.SetAsync(user, "Admin-Set-2", PasswordSetKind.AdminSet);
        Assert.True(user.MustChangePassword);
        Assert.Contains(await w.Db.AuthLogs.ToListAsync(), l => l.Event == AuthEvents.PasswordReset);

        await w.Passwords.SetAsync(user, "My-Own-Pass-3", PasswordSetKind.Self);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public async Task A_new_account_gets_its_password_prepared_without_saving()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.ForceChangeOnAdminSet, "true");
        var user = new User { FirstName = "N", LastName = "U", Email = "new@example.com", RoleId = w.StaffA.RoleId };

        await w.Passwords.PrepareNewAsync(user, "Brand-New-1");
        Assert.True(BCrypt.Net.BCrypt.Verify("Brand-New-1", user.PasswordHash));
        Assert.True(user.MustChangePassword);
        Assert.NotNull(user.PasswordChangedAt);

        w.Db.Users.Add(user);
        await w.Db.SaveChangesAsync();
        Assert.Single(await w.Db.PasswordHistory.Where(h => h.UserId == user.Id).ToListAsync());
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Passwords.PrepareNewAsync(new User(), "x"));
    }

    [Fact]
    public async Task Setting_a_password_lifts_a_lock()
    {
        var (w, user) = await CreateAsync();
        user.LockedUntil = w.Clock.UtcNow.AddMinutes(30);
        user.FailedLoginCount = 4;

        await w.Passwords.SetAsync(user, "Fresh-Start-1", PasswordSetKind.AdminSet);

        Assert.Null(user.LockedUntil);
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public void Aging_says_why_a_new_password_is_needed()
    {
        var now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { CreatedAt = now.AddDays(-100), PasswordChangedAt = now.AddDays(-10) };

        Assert.Null(PasswordAging.ChangeReason(user, 0, now));                       // never expires
        Assert.Null(PasswordAging.ChangeReason(user, 30, now));
        Assert.Equal(PasswordAging.Expired, PasswordAging.ChangeReason(user, 9, now));
        user.PasswordChangedAt = null;                                                 // never changed: counts from creation
        Assert.Equal(PasswordAging.Expired, PasswordAging.ChangeReason(user, 90, now));
        user.MustChangePassword = true;
        Assert.Equal(PasswordAging.Forced, PasswordAging.ChangeReason(user, 0, now)); // forced wins
    }
}

public class SignInSecurityTests
{
    private static AuthService AuthFor(TestWorld w) => new(
        new UserRepository(w.Db),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-signing-key-0123456789-abcdef", ["Jwt:Issuer"] = "t", ["Jwt:Audience"] = "t",
        }).Build(),
        w.Settings, w.Passwords, w.AuthLog, w.Clock);

    private static async Task<User> PersonAsync(TestWorld w, string name = "dave")
    {
        var user = await w.AddUserAsync(name, RoleNames.Staff, w.DeptA.Id);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Right-Pass-1", 4);
        await w.Db.SaveChangesAsync();
        return user;
    }

    private static Task<AuthResponse> LoginAsync(AuthService auth, string name, string password) =>
        auth.LoginAsync(new LoginRequest($"{name}@example.com", password));

    // ───────────── lock-out ─────────────

    [Fact]
    public async Task By_default_wrong_passwords_never_lock_an_account()
    {
        var w = await TestWorld.CreateAsync();
        await PersonAsync(w);
        var auth = AuthFor(w);

        for (var i = 0; i < 12; i++)
            await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));

        Assert.NotNull(await LoginAsync(auth, "dave", "Right-Pass-1"));
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_account_even_against_the_right_one_until_the_time_is_up()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        w.Settings.Set(SettingCatalog.Keys.LoginMaxFailures, "3").Set(SettingCatalog.Keys.LoginLockMinutes, "10");
        var auth = AuthFor(w);

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));
        Assert.Equal(w.Clock.UtcNow.AddMinutes(10), user.LockedUntil);

        var locked = await Assert.ThrowsAsync<TooManyRequestsException>(() => LoginAsync(auth, "dave", "Right-Pass-1"));
        Assert.Contains("10", locked.Message);
        Assert.Equal(TimeSpan.FromMinutes(10), locked.RetryAfter);

        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<TooManyRequestsException>(() => LoginAsync(auth, "dave", "Right-Pass-1"));

        w.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await LoginAsync(auth, "dave", "Right-Pass-1"));
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task A_good_sign_in_resets_the_count_and_a_lock_starts_a_fresh_count()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        w.Settings.Set(SettingCatalog.Keys.LoginMaxFailures, "3");
        var auth = AuthFor(w);

        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));
        Assert.Equal(2, user.FailedLoginCount);
        await LoginAsync(auth, "dave", "Right-Pass-1");
        Assert.Equal(0, user.FailedLoginCount);

        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));
        Assert.Equal(0, user.FailedLoginCount);                    // the lock took the count back to zero
        Assert.NotNull(user.LockedUntil);
    }

    [Fact]
    public async Task An_administrator_can_unlock_and_the_user_list_shows_who_is_locked()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        w.Settings.Set(SettingCatalog.Keys.LoginMaxFailures, "2");
        var auth = AuthFor(w);
        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));
        var org = new OrgAdminService(w.Db, w.Passwords, w.AuthLog, w.Clock);

        var listed = (await org.SearchUsersAsync(w.SuperAdmin.Id, new UserQuery(Search: "dave"))).Items.Single();
        Assert.Equal(user.LockedUntil, listed.LockedUntil);

        var unlocked = await org.UnlockUserAsync(w.SuperAdmin.Id, user.Id);
        Assert.Null(unlocked.LockedUntil);
        Assert.NotNull(await LoginAsync(auth, "dave", "Right-Pass-1"));
        Assert.Contains(await w.Db.AuthLogs.ToListAsync(), l => l.Event == AuthEvents.AccountUnlocked);
    }

    [Fact]
    public async Task A_lock_that_has_run_out_is_not_shown_in_the_list()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        user.LockedUntil = w.Clock.UtcNow.AddMinutes(5);
        await w.Db.SaveChangesAsync();
        var org = new OrgAdminService(w.Db, w.Passwords, w.AuthLog, w.Clock);

        Assert.NotNull((await org.SearchUsersAsync(w.SuperAdmin.Id, new UserQuery(Search: "dave"))).Items.Single().LockedUntil);
        w.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Null((await org.SearchUsersAsync(w.SuperAdmin.Id, new UserQuery(Search: "dave"))).Items.Single().LockedUntil);
    }

    // ───────────── the sign-in log ─────────────

    [Fact]
    public async Task Every_sign_in_outcome_is_logged_with_its_reason()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        var retired = await w.AddUserAsync("retired", RoleNames.Staff, null, active: false);
        w.Settings.Set(SettingCatalog.Keys.LoginMaxFailures, "2");
        var auth = AuthFor(w);

        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "nobody", "x"));
        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "retired", "x"));
        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));
        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "dave", "wrong"));          // locks
        await Assert.ThrowsAsync<TooManyRequestsException>(() => LoginAsync(auth, "dave", "Right-Pass-1"));
        user.LockedUntil = null;
        await w.Db.SaveChangesAsync();
        await LoginAsync(auth, "dave", "Right-Pass-1");

        var rows = (await w.AuthLog.QueryAsync(new AuthLogQuery(PageSize: 50))).Items;
        var pairs = rows.Select(r => (r.Event, r.Reason)).ToList();
        Assert.Contains((AuthEvents.LoginFailed, "unknown_user"), pairs);
        Assert.Contains((AuthEvents.LoginFailed, "inactive"), pairs);
        Assert.Equal(2, pairs.Count(p => p == (AuthEvents.LoginFailed, "bad_password")));
        Assert.Contains((AuthEvents.AccountLocked, "too_many_failures"), pairs);
        Assert.Contains((AuthEvents.LoginFailed, "locked"), pairs);
        Assert.Contains((AuthEvents.LoginSuccess, null), pairs);
        Assert.Equal("nobody@example.com", rows.Single(r => r.Reason == "unknown_user").Email);
        Assert.Equal(retired.Id, rows.Single(r => r.Reason == "inactive").UserId);
    }

    [Fact]
    public async Task The_log_can_be_filtered_searched_paged_and_purged()
    {
        var w = await TestWorld.CreateAsync();
        await PersonAsync(w, "erin");
        var auth = AuthFor(w);
        await LoginAsync(auth, "erin", "Right-Pass-1");                                                  // t0
        w.Clock.Advance(TimeSpan.FromDays(2));
        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "erin", "wrong"));  // t0+2d
        await Assert.ThrowsAsync<RequestValidationException>(() => LoginAsync(auth, "ghost", "wrong"));
        await LoginAsync(auth, "erin", "Right-Pass-1");

        Assert.Equal(4, (await w.AuthLog.QueryAsync(new AuthLogQuery())).Total);
        Assert.Equal(2, (await w.AuthLog.QueryAsync(new AuthLogQuery(Success: false))).Total);
        Assert.Equal(2, (await w.AuthLog.QueryAsync(new AuthLogQuery(Event: AuthEvents.LoginSuccess))).Total);
        Assert.Equal(1, (await w.AuthLog.QueryAsync(new AuthLogQuery(Search: "GHOST"))).Total);
        Assert.Equal(3, (await w.AuthLog.QueryAsync(new AuthLogQuery(From: w.Clock.UtcNow.AddDays(-1)))).Total);
        Assert.Equal(1, (await w.AuthLog.QueryAsync(new AuthLogQuery(To: w.Clock.UtcNow.AddDays(-1)))).Total);

        var page = await w.AuthLog.QueryAsync(new AuthLogQuery(Page: 2, PageSize: 3));
        Assert.Equal((4, 1), (page.Total, page.Items.Count));
        Assert.True((await w.AuthLog.QueryAsync(new AuthLogQuery(PageSize: 3))).Items.First().At >= page.Items.Single().At);   // newest first

        Assert.Equal(1, await w.AuthLog.PurgeOlderThanAsync(w.Clock.UtcNow.AddDays(-1)));
        Assert.Equal(3, (await w.AuthLog.QueryAsync(new AuthLogQuery())).Total);
    }

    [Fact]
    public async Task The_daily_job_removes_old_sign_in_logs_by_the_retention_setting()
    {
        var w = await TestWorld.CreateAsync();
        w.Db.AuthLogs.AddRange(
            new AuthLog { Event = AuthEvents.LoginSuccess, Success = true, At = w.Clock.UtcNow.AddDays(-200) },
            new AuthLog { Event = AuthEvents.LoginSuccess, Success = true, At = w.Clock.UtcNow.AddDays(-10) });
        await w.Db.SaveChangesAsync();

        await new KPI.Infrastructure.Jobs.MaintenanceJob(w.Issues, w.Notifications, w.Db, w.Clock, w.Settings, w.AuthLog,
            NullLogger<KPI.Infrastructure.Jobs.MaintenanceJob>.Instance).RunAsync();

        Assert.Single(await w.Db.AuthLogs.ToListAsync());
    }

    [Fact]
    public async Task A_failure_to_write_the_log_never_blocks_a_sign_in()
    {
        var w = await TestWorld.CreateAsync();
        await PersonAsync(w);
        var broken = new AuthLogService(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("elsewhere").Options),
            KPI.Application.Audit.NullAuditContext.Instance, w.Clock, NullLogger<AuthLogService>.Instance);
        await broken.WriteAsync(AuthEvents.LoginSuccess, true, null, new string('x', 1000));   // over-long values are cut, not thrown
        var auth = new AuthService(new UserRepository(w.Db), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-signing-key-0123456789-abcdef", ["Jwt:Issuer"] = "t", ["Jwt:Audience"] = "t",
        }).Build(), w.Settings, w.Passwords, new ThrowingLog(), w.Clock);

        Assert.NotNull(await auth.LoginAsync(new LoginRequest("dave@example.com", "Right-Pass-1")));
    }

    private sealed class ThrowingLog : IAuthLog
    {
        public Task WriteAsync(string eventName, bool success, Guid? userId, string? email, string? reason = null, string? detail = null) => Task.CompletedTask;
        public Task<KPI.Application.Common.PagedResult<AuthLogDto>> QueryAsync(AuthLogQuery query) => throw new NotSupportedException();
        public Task<int> PurgeOlderThanAsync(DateTime cutoff) => throw new NotSupportedException();
    }

    // ───────────── forced change, expiry and session length ─────────────

    [Fact]
    public async Task Login_and_profile_tell_the_client_when_a_new_password_is_needed()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        var auth = AuthFor(w);

        var plain = await LoginAsync(auth, "dave", "Right-Pass-1");
        Assert.False(plain.PasswordChangeRequired);

        user.MustChangePassword = true;
        await w.Db.SaveChangesAsync();
        var forced = await LoginAsync(auth, "dave", "Right-Pass-1");
        Assert.True(forced.PasswordChangeRequired);
        Assert.Equal("forced", forced.PasswordChangeReason);
        Assert.Equal("forced", (await auth.GetProfileAsync(user.Id)).PasswordChangeReason);

        await auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Right-Pass-1", "Brand-New-Pass-2"));
        var after = await auth.GetProfileAsync(user.Id);
        Assert.False(after.PasswordChangeRequired);
        Assert.Null(after.PasswordChangeReason);
    }

    [Fact]
    public async Task A_password_older_than_the_limit_must_be_changed_and_a_change_starts_the_clock_again()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        w.Settings.Set(SettingCatalog.Keys.PasswordMaxAgeDays, "30");
        var auth = AuthFor(w);
        await auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Right-Pass-1", "Second-Pass-2"));

        w.Clock.Advance(TimeSpan.FromDays(29));
        Assert.False((await auth.GetProfileAsync(user.Id)).PasswordChangeRequired);
        w.Clock.Advance(TimeSpan.FromDays(2));
        var expired = await auth.GetProfileAsync(user.Id);
        Assert.True(expired.PasswordChangeRequired);
        Assert.Equal("expired", expired.PasswordChangeReason);

        await auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Second-Pass-2", "Third-Pass-3"));
        Assert.False((await auth.GetProfileAsync(user.Id)).PasswordChangeRequired);
    }

    [Fact]
    public async Task The_token_lasts_as_many_days_as_the_session_setting()
    {
        var w = await TestWorld.CreateAsync();
        await PersonAsync(w);
        var auth = AuthFor(w);

        var seven = new JwtSecurityTokenHandler().ReadJwtToken((await LoginAsync(auth, "dave", "Right-Pass-1")).Token);
        w.Settings.Set(SettingCatalog.Keys.SessionDays, "2");
        var two = new JwtSecurityTokenHandler().ReadJwtToken((await LoginAsync(auth, "dave", "Right-Pass-1")).Token);

        Assert.InRange((seven.ValidTo - DateTime.UtcNow).TotalDays, 6.9, 7.1);
        Assert.InRange((two.ValidTo - DateTime.UtcNow).TotalDays, 1.9, 2.1);
    }

    [Fact]
    public async Task Changing_ones_own_password_follows_the_policy_and_the_history()
    {
        var w = await TestWorld.CreateAsync();
        var user = await PersonAsync(w);
        w.Settings.Set(SettingCatalog.Keys.PasswordRequireDigit, "true").Set(SettingCatalog.Keys.PasswordHistoryCount, "2");
        var auth = AuthFor(w);

        await Assert.ThrowsAsync<RequestValidationException>(() => auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Right-Pass-1", "no-digits-here")));
        await Assert.ThrowsAsync<RequestValidationException>(() => auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("wrong", "New-Pass-2-ok")));
        await auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("Right-Pass-1", "New-Pass-2-ok"));
        await Assert.ThrowsAsync<RequestValidationException>(() => auth.ChangePasswordAsync(user.Id, new ChangePasswordRequest("New-Pass-2-ok", "Right-Pass-1")));  // reuse
    }
}

public class SecurityApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public SecurityApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> ClientForAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return client;
    }

    private async Task MutateAsync(string email, Action<User> change)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.Users.FirstAsync(u => u.Email == email));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Someone_who_must_choose_a_new_password_can_only_see_their_profile_and_change_it()
    {
        var client = await _factory.ClientAsync("viewer-a");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/issues")).StatusCode);

        await MutateAsync("viewer-a@example.com", u => u.MustChangePassword = true);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/issues")).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("passwordChangeRequired").GetBoolean());
        Assert.Equal("forced", me.GetProperty("passwordChangeReason").GetString());

        var changed = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = ApiFactory.Password, newPassword = "A-New-Password-9" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/issues")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("passwordChangeRequired").GetBoolean());
    }

    [Fact]
    public async Task Expiry_applies_to_everyone_when_it_is_switched_on()
    {
        var admin = await _factory.ClientAsync("admin");
        var staff = await _factory.ClientAsync("staff-b");
        await MutateAsync("staff-b@example.com", u => u.PasswordChangedAt = DateTime.UtcNow.AddDays(-100));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/issues")).StatusCode);        // expiry is off by default

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/security.password.maxAgeDays", new { value = "90" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/issues")).StatusCode);
        var me = await staff.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("expired", me.GetProperty("passwordChangeReason").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/issues")).StatusCode);        // the admin's own password is fresh

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/settings/security.password.maxAgeDays")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/issues")).StatusCode);
    }

    [Fact]
    public async Task The_policy_is_published_and_applied_when_an_administrator_creates_a_user()
    {
        var admin = await _factory.ClientAsync("admin");
        var policy = await admin.GetFromJsonAsync<JsonElement>("/api/auth/password-policy");
        Assert.Equal(8, policy.GetProperty("minLength").GetInt32());

        string roleId;
        using (var scope = _factory.Services.CreateScope())
            roleId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Roles.FirstAsync(r => r.Name == "Staff")).Id.ToString();
        object Body(string email, string password) => new { firstName = "N", lastName = "U", email, password, roleId, departmentId = (string?)null };

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/security.password.minLength", new { value = "12" })).StatusCode);
        Assert.Equal(12, (await admin.GetFromJsonAsync<JsonElement>("/api/auth/password-policy")).GetProperty("minLength").GetInt32());
        var tooShort = await admin.PostAsJsonAsync("/api/users", Body("policy1@example.com", "Too-Short-1"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("12", (await tooShort.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/users", Body("policy2@example.com", "Long-Enough-Pass-1"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/settings/security.password.minLength")).StatusCode);
    }

    [Fact]
    public async Task A_user_made_by_an_administrator_must_choose_their_own_password_when_the_setting_is_on()
    {
        var admin = await _factory.ClientAsync("admin");
        string roleId;
        using (var scope = _factory.Services.CreateScope())
            roleId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Roles.FirstAsync(r => r.Name == "Staff")).Id.ToString();
        await admin.PutAsJsonAsync("/api/settings/security.forceChangeOnAdminSet", new { value = "true" });

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/users",
            new { firstName = "F", lastName = "C", email = "forced@example.com", password = "Given-By-Admin-1", roleId, departmentId = (string?)null })).StatusCode);
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "forced@example.com", password = "Given-By-Admin-1" });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("passwordChangeRequired").GetBoolean());
        Assert.Equal("forced", body.GetProperty("passwordChangeReason").GetString());

        await admin.DeleteAsync("/api/settings/security.forceChangeOnAdminSet");
    }

    [Fact]
    public async Task The_sign_in_log_is_for_administrators_and_shows_what_happened()
    {
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "manager-b@example.com", password = "wrong-one" });
        await _factory.TokenAsync("manager-b");
        var admin = await _factory.ClientAsync("admin");
        var staff = await _factory.ClientAsync("staff-a");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/auth-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/auth-logs")).StatusCode);

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/auth-logs?search=manager-b&pageSize=10");
        var events = page.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("event").GetString(), i.GetProperty("success").GetBoolean())).ToList();
        Assert.Contains((AuthEvents.LoginFailed, false), events);
        Assert.Contains((AuthEvents.LoginSuccess, true), events);

        var failed = await admin.GetFromJsonAsync<JsonElement>("/api/auth-logs?success=false&search=manager-b");
        Assert.All(failed.GetProperty("items").EnumerateArray(), i => Assert.False(i.GetProperty("success").GetBoolean()));
        var all = await admin.GetFromJsonAsync<string[]>("/api/auth-logs/events");
        Assert.Equal(AuthEvents.All, all);
    }

    [Fact]
    public async Task An_administrator_unlocks_an_account_from_the_users_list()
    {
        var admin = await _factory.ClientAsync("admin");
        var staff = await _factory.ClientAsync("staff-a");
        await MutateAsync("director@example.com", u => { u.LockedUntil = DateTime.UtcNow.AddMinutes(20); u.FailedLoginCount = 0; });
        var id = _factory.UserId("director");

        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/users?search=director")).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("email").GetString() == "director@example.com");
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("lockedUntil").ValueKind);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/users/{id}/unlock", null)).StatusCode);
        var unlocked = await admin.PostAsync($"/api/users/{id}/unlock", null);
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await unlocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lockedUntil").ValueKind);
    }
}
