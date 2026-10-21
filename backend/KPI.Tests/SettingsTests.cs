using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.Application.Issues;
using KPI.Application.Settings;
using KPI.Domain.Constants;
using KPI.Domain.Exceptions;
using KPI.Domain.Issues;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class SettingsCatalogTests
{
    [Fact]
    public void Keys_are_unique_and_every_default_is_valid_and_within_its_limits()
    {
        Assert.Equal(SettingCatalog.All.Count, SettingCatalog.All.Select(d => d.Key).Distinct().Count());
        foreach (var d in SettingCatalog.All)
        {
            Assert.Equal(d.Default, SettingCatalog.Normalize(d, d.Default));
            Assert.False(string.IsNullOrWhiteSpace(d.Label) || string.IsNullOrWhiteSpace(d.Description), d.Key);
            if (d.Type == SettingType.Number) Assert.True(d.Min <= double.Parse(d.Default) && double.Parse(d.Default) <= d.Max, d.Key);
        }
    }

    [Fact]
    public void The_defaults_are_the_targets_the_system_shipped_with()
    {
        var sla = new DefaultSettings().SlaAsync().Result;
        Assert.Equal(IssueSlaPolicy.Default, sla);
    }

    [Theory]
    [InlineData(SettingCatalog.Keys.MaxAttachments, "0")]
    [InlineData(SettingCatalog.Keys.MaxAttachments, "7")]
    [InlineData(SettingCatalog.Keys.AutoCloseDays, "abc")]
    [InlineData(SettingCatalog.Keys.AutoCloseDays, "")]
    [InlineData(SettingCatalog.Keys.EmailEnabled, "yes")]
    [InlineData(SettingCatalog.Keys.RequestLogDays, "3")]
    [InlineData(SettingCatalog.Keys.ResolveHoursLow, "NaN")]
    public void A_value_outside_the_type_or_limits_is_refused(string key, string value) =>
        Assert.Throws<RequestValidationException>(() => SettingCatalog.Normalize(SettingCatalog.Find(key)!, value));

    [Fact]
    public void Numbers_and_switches_are_stored_in_one_canonical_form()
    {
        Assert.Equal("2.5", SettingCatalog.Normalize(SettingCatalog.Find(SettingCatalog.Keys.FirstResponseHours)!, " 2.50 "));
        Assert.Equal("true", SettingCatalog.Normalize(SettingCatalog.Find(SettingCatalog.Keys.EmailEnabled)!, "TRUE"));
    }
}

public class SettingsAdminTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public Guid AdminId { get; private set; }
        public ISystemSettings Settings => Provider.GetRequiredService<ISystemSettings>();

        public Fixture()
        {
            var name = Guid.NewGuid().ToString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddSingleton<ISecretProtector>(new SecretProtector("unit-test-signing-key-0123456789"));
            services.AddSingleton<ISystemSettings, SystemSettingsService>();
            services.AddScoped<ISettingsAdminService, SettingsAdminService>();
            Provider = services.BuildServiceProvider();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            using var scope = f.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await DbSeeder.SeedAsync(db);
            var admin = new KPI.Domain.Entities.User
            {
                FirstName = "Ada", LastName = "Min", Email = "ada@example.com", PasswordHash = "x", IsActive = true,
                RoleId = (await db.Roles.FirstAsync(r => r.Name == RoleNames.SuperAdmin)).Id,
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            f.AdminId = admin.Id;
            return f;
        }

        public ISettingsAdminService Admin(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISettingsAdminService>();
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    [Fact]
    public async Task Every_setting_is_listed_at_its_default_until_somebody_changes_it()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Provider.CreateScope();

        var list = await f.Admin(scope).ListAsync();

        Assert.Equal(SettingCatalog.All.Count, list.Count);
        Assert.All(list, s => Assert.True(s.IsDefault && s.Value == s.Default && s.UpdatedAt is null));
    }

    [Fact]
    public async Task A_change_is_saved_remembered_and_seen_at_once_by_the_rest_of_the_system()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Provider.CreateScope();
        var admin = f.Admin(scope);
        Assert.Equal(72, await f.Settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursMedium)); // cached at the default

        var changed = await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.ResolveHoursMedium, new SaveSettingRequest("48"));

        Assert.False(changed.IsDefault);
        Assert.Equal("48", changed.Value);
        Assert.Equal("Ada Min", changed.UpdatedByName);
        Assert.Equal(48, await f.Settings.GetNumberAsync(SettingCatalog.Keys.ResolveHoursMedium));
        Assert.Equal(TimeSpan.FromHours(48), (await f.Settings.SlaAsync()).ResolveMedium);
        var history = Assert.Single(await admin.HistoryAsync(SettingCatalog.Keys.ResolveHoursMedium));
        Assert.Equal(("72", "48", "Ada Min"), (history.OldValue, history.NewValue, history.ChangedByName));
    }

    [Fact]
    public async Task Setting_the_same_value_again_changes_nothing_and_leaves_no_history()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Provider.CreateScope();
        var admin = f.Admin(scope);

        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.ResolveHoursMedium, new SaveSettingRequest("72"));   // already the default
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.AutoCloseDays, new SaveSettingRequest("5"));
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.AutoCloseDays, new SaveSettingRequest("5.0"));

        Assert.Empty(await admin.HistoryAsync(SettingCatalog.Keys.ResolveHoursMedium));
        Assert.Single(await admin.HistoryAsync(SettingCatalog.Keys.AutoCloseDays));
    }

    [Fact]
    public async Task Putting_a_value_back_to_the_default_removes_the_override_but_keeps_the_history()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Provider.CreateScope();
        var admin = f.Admin(scope);
        await admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.EmailEnabled, new SaveSettingRequest("false"));
        Assert.False(await f.Settings.GetBoolAsync(SettingCatalog.Keys.EmailEnabled));

        var reset = await admin.ResetAsync(f.AdminId, SettingCatalog.Keys.EmailEnabled);

        Assert.True(reset.IsDefault);
        Assert.True(await f.Settings.GetBoolAsync(SettingCatalog.Keys.EmailEnabled));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.ToListAsync());
        Assert.Equal(2, (await admin.HistoryAsync(SettingCatalog.Keys.EmailEnabled)).Count);
    }

    [Fact]
    public async Task Invalid_values_and_unknown_keys_are_refused_without_touching_anything()
    {
        await using var f = await Fixture.CreateAsync();
        using var scope = f.Provider.CreateScope();
        var admin = f.Admin(scope);

        await Assert.ThrowsAsync<RequestValidationException>(() => admin.UpdateAsync(f.AdminId, SettingCatalog.Keys.MaxAttachments, new SaveSettingRequest("50")));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.UpdateAsync(f.AdminId, "no.such.key", new SaveSettingRequest("1")));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.HistoryAsync("no.such.key"));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.ToListAsync());
    }
}

/// <summary>The settings actually drive the behaviour they describe.</summary>
public class SettingsBehaviourTests
{
    private static CreateIssueRequest Report() =>
        new("เครื่องพิมพ์ชั้น 2 ไม่ออก", "สั่งพิมพ์แล้วไม่มีอะไรออกมา ไฟสถานะเป็นสีแดง", IssueCategory.Bug, IssueModule.Application, IssueImpact.Degraded);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Fact]
    public async Task New_issues_get_the_deadlines_from_the_settings()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.FirstResponseHours, "1").Set(SettingCatalog.Keys.ResolveHoursMedium, "10");

        var issue = await w.Issues.CreateAsync(Report(), [], w.StaffA.Id, null);

        Assert.Equal(issue.CreatedAt.AddHours(1), issue.ResponseDueAt);
        Assert.Equal(issue.CreatedAt.AddHours(10), issue.ResolveDueAt);
    }

    [Fact]
    public async Task A_priority_change_uses_the_current_windows()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.ResolveHoursHigh, "12");
        var issue = await w.Issues.CreateAsync(Report(), [], w.StaffA.Id, null);        // Medium: 72 h

        var raised = await w.Issues.SetPriorityAsync(issue.Id, IssuePriority.High, w.ItA.Id);

        Assert.Equal(issue.CreatedAt.AddHours(12), raised.ResolveDueAt);
    }

    [Fact]
    public async Task Resolved_issues_are_closed_after_the_number_of_days_in_the_settings()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.AutoCloseDays, "10");
        var issue = await w.Issues.CreateAsync(Report(), [], w.StaffA.Id, null);
        await w.Issues.StartAsync(issue.Id, w.ItA.Id);
        await w.Issues.ResolveAsync(issue.Id, new ResolveIssueRequest("เปลี่ยนตลับหมึกแล้ว", IssueRootCause.Other), w.ItA.Id);

        w.Clock.Advance(TimeSpan.FromDays(9));
        Assert.Equal(0, await w.Issues.AutoCloseResolvedAsync(w.Clock.UtcNow));
        w.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await w.Issues.AutoCloseResolvedAsync(w.Clock.UtcNow));
    }

    [Fact]
    public async Task The_attachment_limits_come_from_the_settings()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.MaxAttachments, "2");
        var three = Enumerable.Range(0, 3).Select(i => new IssueUpload($"{i}.png", "image/png", Png)).ToList();
        await Assert.ThrowsAsync<RequestValidationException>(() => w.Issues.CreateAsync(Report(), three, w.StaffA.Id, null));
        await w.Issues.CreateAsync(Report(), three.Take(2).ToList(), w.StaffA.Id, null);

        w.Settings.Set(SettingCatalog.Keys.MaxAttachmentMb, "1");
        var big = new byte[1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            w.Issues.CreateAsync(Report(), [new IssueUpload("big.png", "image/png", big)], w.StaffA.Id, null));
    }

    [Fact]
    public async Task Turning_email_off_in_the_settings_stops_the_mails_but_not_the_issue()
    {
        var w = await TestWorld.CreateAsync();
        w.Settings.Set(SettingCatalog.Keys.EmailEnabled, "false");

        var issue = await w.Issues.CreateAsync(Report(), [], w.StaffA.Id, null);

        Assert.NotNull(issue.Number);
        Assert.Empty(w.Email.IssueSent);
    }
}

public class SettingsApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public SettingsApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Only_the_administrator_may_read_or_change_the_settings()
    {
        var staff = await _factory.ClientAsync("staff-a");
        var admin = await _factory.ClientAsync("admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/settings/issues.autoCloseDays", new { value = "5" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/settings")).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal(SettingCatalog.All.Count, list.GetArrayLength());
    }

    [Fact]
    public async Task A_change_through_the_api_takes_effect_and_can_be_reset()
    {
        var admin = await _factory.ClientAsync("admin");

        var ok = await admin.PutAsJsonAsync("/api/settings/issues.maxAttachments", new { value = "3" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("3", body.GetProperty("value").GetString());
        Assert.False(body.GetProperty("isDefault").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/settings/issues.maxAttachments", new { value = "99" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/settings/nope", new { value = "1" })).StatusCode);

        var history = await admin.GetFromJsonAsync<JsonElement>("/api/settings/issues.maxAttachments/history");
        Assert.Equal(1, history.GetArrayLength());

        var reset = await admin.DeleteAsync("/api/settings/issues.maxAttachments");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.True((await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isDefault").GetBoolean());
    }
}
