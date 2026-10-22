using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.API.Controllers;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace KPI.Tests;

public class CompanyNameRulesTests
{
    private static IConfiguration Config(string? installed) =>
        new ConfigurationBuilder().AddInMemoryCollection(installed is null ? [] : new Dictionary<string, string?> { ["App:CompanyName"] = installed }).Build();

    [Fact]
    public void What_the_administrator_set_wins_then_what_the_installer_was_given_then_nothing()
    {
        Assert.Equal("บริษัท ก จำกัด", ClientConfigController.CompanyName("  บริษัท ก จำกัด ", Config("Installed Co")));
        Assert.Equal("Installed Co", ClientConfigController.CompanyName("", Config(" Installed Co ")));
        Assert.Equal("Installed Co", ClientConfigController.CompanyName(null, Config("Installed Co")));
        Assert.Equal(string.Empty, ClientConfigController.CompanyName("", Config(null)));
    }

    [Fact]
    public void A_name_with_a_line_break_or_that_is_too_long_is_not_shown_as_it_is()
    {
        Assert.Equal("Installed Co", ClientConfigController.CompanyName("Evil\r\nName", Config("Installed Co")));
        Assert.Equal(80, ClientConfigController.CompanyName(new string('ก', 120), Config(null)).Length);
    }

    [Theory]
    [InlineData("Acme", "Acme")]
    [InlineData("  บริษัท ตัวอย่าง จำกัด (มหาชน)  ", "บริษัท ตัวอย่าง จำกัด (มหาชน)")]
    [InlineData("", "")]
    public void The_setting_stores_one_trimmed_line(string raw, string expected) =>
        Assert.Equal(expected, SettingCatalog.Normalize(SettingCatalog.Find(SettingCatalog.Keys.CompanyName)!, raw));

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    public void The_setting_refuses_line_breaks_and_control_characters(string raw) =>
        Assert.Throws<RequestValidationException>(() => SettingCatalog.Normalize(SettingCatalog.Find(SettingCatalog.Keys.CompanyName)!, raw));

    [Fact]
    public void The_setting_refuses_more_than_80_characters_but_other_text_settings_keep_their_own_limit()
    {
        var company = SettingCatalog.Find(SettingCatalog.Keys.CompanyName)!;
        Assert.Equal(new string('x', 80), SettingCatalog.Normalize(company, new string('x', 80)));
        Assert.Throws<RequestValidationException>(() => SettingCatalog.Normalize(company, new string('x', 81)));
        Assert.Equal(new string('x', 200), SettingCatalog.Normalize(SettingCatalog.Find(SettingCatalog.Keys.SmtpUsername)!, new string('x', 200)));
    }
}

public class CompanyNameApiTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;
    private readonly ApiFactory _factory;

    public CompanyNameApiTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try { if (!_seeded) { await _factory.SeedAsync(); _seeded = true; } }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> PublicNameAsync()
    {
        // before anyone signs in: no token
        var json = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/client-config");
        return json.GetProperty("companyName").GetString()!;
    }

    [Fact]
    public async Task The_name_set_in_the_settings_is_shown_before_sign_in_and_reset_removes_it()
    {
        var admin = await _factory.ClientAsync("admin");
        Assert.Equal(string.Empty, await PublicNameAsync());

        var set = await admin.PutAsJsonAsync("/api/settings/org.companyName", new { value = "  บริษัท ตัวอย่าง จำกัด " });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("บริษัท ตัวอย่าง จำกัด", await PublicNameAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/settings/org.companyName", new { value = "a\nb" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/settings/org.companyName", new { value = new string('x', 81) })).StatusCode);
        Assert.Equal("บริษัท ตัวอย่าง จำกัด", await PublicNameAsync());   // refused changes changed nothing

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync("/api/settings/org.companyName")).StatusCode);
        Assert.Equal(string.Empty, await PublicNameAsync());
    }

    [Fact]
    public async Task Only_an_administrator_may_change_the_name()
    {
        var staff = await _factory.ClientAsync("staff-a");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/settings/org.companyName", new { value = "Mine" })).StatusCode);
    }
}

public class CompanyNameFromInstallerTests
{
    private sealed class Installed : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("App:CompanyName", "Installed Co");
        }
    }

    [Fact]
    public async Task The_name_the_installer_was_given_is_shown_until_an_administrator_sets_another()
    {
        using var factory = new Installed();
        await factory.SeedAsync();

        var before = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/client-config");
        Assert.Equal("Installed Co", before.GetProperty("companyName").GetString());

        var admin = await factory.ClientAsync("admin");
        await admin.PutAsJsonAsync("/api/settings/org.companyName", new { value = "Chosen Co" });
        var after = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/client-config");
        Assert.Equal("Chosen Co", after.GetProperty("companyName").GetString());
    }
}
