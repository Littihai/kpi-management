using System.Net;
using System.Net.Http.Json;
using KPI.API.Audit;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KPI.Tests;

/// <summary>The rationing of request-log rows written for people who are not signed in.</summary>
public class AnonymousRequestLogGateTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void One_address_gets_its_share_each_minute_and_others_are_unaffected()
    {
        var gate = new AnonymousRequestLogGate(perAddressPerMinute: 3, overallPerMinute: 100, _clock);

        Assert.All(Enumerable.Range(0, 3), _ => Assert.True(gate.Allow("6.6.6.6")));
        Assert.False(gate.Allow("6.6.6.6"));
        Assert.True(gate.Allow("10.0.0.5"));
    }

    [Fact]
    public void The_share_comes_back_with_the_next_minute()
    {
        var gate = new AnonymousRequestLogGate(2, 100, _clock);
        gate.Allow("6.6.6.6");
        gate.Allow("6.6.6.6");
        Assert.False(gate.Allow("6.6.6.6"));

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(gate.Allow("6.6.6.6"));
    }

    [Fact]
    public void Many_addresses_together_cannot_exceed_the_overall_share()
    {
        var gate = new AnonymousRequestLogGate(perAddressPerMinute: 10, overallPerMinute: 5, _clock);

        var allowed = Enumerable.Range(0, 20).Count(i => gate.Allow($"198.51.100.{i}"));

        Assert.Equal(5, allowed);
    }

    [Fact]
    public void A_share_of_zero_writes_nothing_for_anonymous_callers()
    {
        var gate = new AnonymousRequestLogGate(0, 0, _clock);
        Assert.False(gate.Allow("1.1.1.1"));
    }

    [Fact]
    public void Defaults_and_configuration()
    {
        var perAddress = AnonymousRequestLogGate.From(new ConfigurationBuilder().Build(), _clock);
        Assert.Equal(20, Enumerable.Range(0, 100).Count(_ => perAddress.Allow("1.1.1.1")));

        var overall = AnonymousRequestLogGate.From(new ConfigurationBuilder().Build(), _clock);
        Assert.Equal(300, Enumerable.Range(0, 1000).Count(i => overall.Allow($"10.0.{i / 250}.{i % 250}")));

        var configured = AnonymousRequestLogGate.From(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:AnonymousPerAddressPerMinute"] = "4" }).Build(),
            _clock);
        Assert.Equal(4, Enumerable.Range(0, 100).Count(_ => configured.Allow("2.2.2.2")));
    }
}

/// <summary>
/// The app told to ration anonymous log rows to 3 per address per minute, reading the address from X-Test-Ip. The gate runs
/// on a clock that never moves, so a test cannot straddle a minute boundary and see the allowance renewed halfway.
/// </summary>
public class AnonymousLogFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginThrottle:ClientIpHeader", "X-Test-Ip");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AnonymousRequestLogGate>();
            services.AddSingleton(new AnonymousRequestLogGate(perAddressPerMinute: 3, overallPerMinute: 300, new ManualTimeProvider()));
        });
    }
}

public class AnonymousRequestLogApiTests : IClassFixture<AnonymousLogFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private readonly AnonymousLogFactory _factory;

    public AnonymousRequestLogApiTests(AnonymousLogFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded) { await _factory.SeedAsync(); _seeded = true; }
        }
        finally { SeedLock.Release(); }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Anonymous(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Ip", ip);
        return client;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The expected record did not appear in time.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task Anonymous_requests_for_paths_that_do_not_exist_are_not_logged()
    {
        var ip = $"198.51.100.{Random.Shared.Next(100, 200)}";
        var client = Anonymous(ip);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/nothing-here-{i}")).StatusCode);
        await client.PostAsJsonAsync("/api/auth/login", new { email = "someone@example.com", password = "wrong-password" }); // marker

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await WaitUntilAsync(() => db.RequestLogs.Any(r => r.IpAddress == ip && r.Path == "/api/auth/login"));

        Assert.DoesNotContain(await db.RequestLogs.AsNoTracking().Where(r => r.IpAddress == ip).ToListAsync(), r => r.StatusCode == 404);
    }

    [Fact]
    public async Task A_flood_from_one_address_writes_only_its_share_but_a_signed_in_refusal_is_still_recorded()
    {
        var ip = $"192.0.2.{Random.Shared.Next(10, 250)}";
        var flood = Anonymous(ip);
        for (var i = 0; i < 8; i++)
            await flood.PostAsJsonAsync("/api/auth/login", new { email = $"ghost{i}@example.com", password = "wrong-password" });

        // A signed-in colleague on the same office address is refused something: that must still be written.
        var staff = await _factory.ClientAsync("staff-a");
        staff.DefaultRequestHeaders.Add("X-Test-Ip", ip);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/users")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await WaitUntilAsync(() => db.RequestLogs.Any(r => r.IpAddress == ip && r.Path == "/api/users"));
        await Task.Delay(300); // let any stragglers be saved

        var rows = await db.RequestLogs.AsNoTracking().Where(r => r.IpAddress == ip).ToListAsync();
        Assert.Equal(3, rows.Count(r => r.Path == "/api/auth/login"));
        Assert.Single(rows, r => r.Path == "/api/users" && r.StatusCode == 403);
    }
}

/// <summary>The whole-form ceiling, seen through the real endpoint: 3 reports a day, 1 MB of pictures a day.</summary>
public class PublicCeilingFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginThrottle:ClientIpHeader", "X-Test-Ip");
        builder.UseSetting("PublicIssues:MaxPerWindow", "50");
        builder.UseSetting("PublicIssues:MaxPerDay", "3");
        builder.UseSetting("PublicIssues:MaxUploadMbPerDay", "1");
    }
}

public class PublicCeilingApiTests : IClassFixture<PublicCeilingFactory>
{
    private readonly PublicCeilingFactory _factory;

    public PublicCeilingApiTests(PublicCeilingFactory factory) => _factory = factory;

    private static MultipartFormDataContent Report(int pictureKilobytes = 0)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("สมหญิง ใจดี"), "ReporterName" },
            { new StringContent("somying@example.com"), "ReporterEmail" },
            { new StringContent("เข้าสู่ระบบไม่ได้เลย"), "Title" },
            { new StringContent("ใส่รหัสผ่านถูกแล้วแต่ขึ้นว่าไม่ถูกต้อง"), "Description" },
            { new StringContent("Bug"), "Category" },
            { new StringContent("Login"), "Module" },
            { new StringContent("Blocking"), "Impact" },
        };
        if (pictureKilobytes > 0)
        {
            var data = new byte[pictureKilobytes * 1024];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
            var file = new ByteArrayContent(data);
            file.Headers.ContentType = new("image/png");
            form.Add(file, "Files", "screen.png");
        }
        return form;
    }

    private async Task<HttpResponseMessage> SendAsync(string ip, int pictureKilobytes = 0)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Ip", ip);
        return await client.PostAsync("/api/public/issues", Report(pictureKilobytes));
    }

    [Fact]
    public async Task Many_different_addresses_together_cannot_exceed_the_reports_and_pictures_allowed_per_day()
    {
        // Pictures from different addresses are added up: 400 KB + 400 KB fit in 1 MB, a third 400 KB does not.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("203.0.113.1", pictureKilobytes: 400)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("203.0.113.2", pictureKilobytes: 400)).StatusCode);
        var tooBig = await SendAsync("203.0.113.3", pictureKilobytes: 400);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooBig.StatusCode);
        Assert.True(tooBig.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // A report without a picture still fits: this is the third of the three allowed a day.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("203.0.113.4")).StatusCode);

        // Now the day is full of reports, whoever asks and whatever they attach.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync("203.0.113.5")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync("203.0.113.6", pictureKilobytes: 1)).StatusCode);
    }
}
