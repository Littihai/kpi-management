using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KPI.API.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KPI.Tests;

/// <summary>A clock the test moves by hand.</summary>
public class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class LoginThrottleTests
{
    private readonly ManualTimeProvider _clock = new();

    private LoginThrottle Create(int perAccount = 5, int perIp = 20, int windowMinutes = 15) =>
        new(Options.Create(new LoginThrottleOptions
        {
            MaxFailuresPerAccount = perAccount,
            MaxFailuresPerIp = perIp,
            WindowMinutes = windowMinutes,
        }), _clock);

    private static void Fail(LoginThrottle throttle, string ip, string email, int times)
    {
        for (var i = 0; i < times; i++) throttle.RecordFailure(ip, email);
    }

    [Fact]
    public void A_new_client_is_not_blocked()
    {
        Assert.Null(Create().BlockedFor("1.1.1.1", "a@example.com"));
    }

    [Fact]
    public void Blocks_one_account_from_one_address_after_the_limit_and_says_how_long()
    {
        var throttle = Create();
        Fail(throttle, "1.1.1.1", "a@example.com", 4);
        Assert.Null(throttle.BlockedFor("1.1.1.1", "a@example.com"));

        throttle.RecordFailure("1.1.1.1", "a@example.com");

        var wait = throttle.BlockedFor("1.1.1.1", "a@example.com");
        Assert.Equal(TimeSpan.FromMinutes(15), wait);

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(5), throttle.BlockedFor("1.1.1.1", "a@example.com"));
    }

    [Fact]
    public void A_stranger_cannot_lock_out_the_same_account_from_another_address()
    {
        var throttle = Create();
        Fail(throttle, "6.6.6.6", "boss@example.com", 5);

        Assert.NotNull(throttle.BlockedFor("6.6.6.6", "boss@example.com"));
        Assert.Null(throttle.BlockedFor("10.0.0.5", "boss@example.com"));
    }

    [Fact]
    public void One_address_trying_many_accounts_is_stopped_by_the_address_limit()
    {
        var throttle = Create(perAccount: 5, perIp: 20);
        for (var i = 0; i < 20; i++)
            throttle.RecordFailure("6.6.6.6", $"user{i}@example.com"); // one guess per account

        Assert.NotNull(throttle.BlockedFor("6.6.6.6", "user99@example.com"));
        Assert.Null(throttle.BlockedFor("10.0.0.5", "user99@example.com"));
    }

    [Fact]
    public void The_block_lifts_when_the_window_ends_and_counting_starts_over()
    {
        var throttle = Create();
        Fail(throttle, "1.1.1.1", "a@example.com", 5);
        Assert.NotNull(throttle.BlockedFor("1.1.1.1", "a@example.com"));

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(throttle.BlockedFor("1.1.1.1", "a@example.com"));

        Fail(throttle, "1.1.1.1", "a@example.com", 4);
        Assert.Null(throttle.BlockedFor("1.1.1.1", "a@example.com"));
    }

    [Fact]
    public void A_successful_sign_in_forgives_the_account_counter()
    {
        var throttle = Create();
        Fail(throttle, "1.1.1.1", "a@example.com", 4);

        throttle.RecordSuccess("1.1.1.1", "a@example.com");
        Fail(throttle, "1.1.1.1", "a@example.com", 4);

        Assert.Null(throttle.BlockedFor("1.1.1.1", "a@example.com"));
    }

    [Fact]
    public void A_successful_sign_in_does_not_reset_the_address_counter()
    {
        // Otherwise someone with one valid account could sign in between guesses and never hit the limit.
        var throttle = Create(perAccount: 100, perIp: 10);
        for (var i = 0; i < 10; i++)
        {
            throttle.RecordFailure("6.6.6.6", $"victim{i}@example.com");
            throttle.RecordSuccess("6.6.6.6", "mine@example.com");
        }

        Assert.NotNull(throttle.BlockedFor("6.6.6.6", "victim99@example.com"));
    }

    [Fact]
    public void Email_case_and_length_cannot_be_used_to_dodge_or_flood_the_counters()
    {
        var throttle = Create(perAccount: 3);
        var huge = new string('x', 5_000) + "@example.com";
        Fail(throttle, "1.1.1.1", huge, 3);
        Assert.NotNull(throttle.BlockedFor("1.1.1.1", huge));
    }

    [Fact]
    public void A_limit_of_zero_switches_that_check_off()
    {
        var throttle = Create(perAccount: 0, perIp: 0);
        Fail(throttle, "1.1.1.1", "a@example.com", 50);
        Assert.Null(throttle.BlockedFor("1.1.1.1", "a@example.com"));
    }

    // ───────────── client address ─────────────

    private static DefaultHttpContext ContextFrom(string? socketIp, string? headerName = null, string? headerValue = null)
    {
        var context = new DefaultHttpContext();
        if (socketIp is not null) context.Connection.RemoteIpAddress = IPAddress.Parse(socketIp);
        if (headerName is not null) context.Request.Headers[headerName] = headerValue;
        return context;
    }

    [Fact]
    public void Without_a_configured_header_the_socket_address_is_used_even_if_a_header_is_sent()
    {
        var context = ContextFrom("10.1.1.1", "CF-Connecting-IP", "203.0.113.9");
        Assert.Equal("10.1.1.1", LoginThrottle.ClientIp(context, null));
        Assert.Equal("10.1.1.1", LoginThrottle.ClientIp(context, ""));
    }

    [Fact]
    public void The_configured_header_wins_when_it_holds_an_ip_address()
    {
        var context = ContextFrom("10.1.1.1", "CF-Connecting-IP", "203.0.113.9");
        Assert.Equal("203.0.113.9", LoginThrottle.ClientIp(context, "CF-Connecting-IP"));
    }

    [Fact]
    public void Only_the_last_address_of_a_list_is_read_because_the_proxy_appends_it()
    {
        // The proxy saw 10.9.9.9 and added it at the end; whatever stands before it was written by the caller.
        var context = ContextFrom("10.1.1.1", "X-Forwarded-For", "203.0.113.9, 10.9.9.9");
        Assert.Equal("10.9.9.9", LoginThrottle.ClientIp(context, "X-Forwarded-For"));
    }

    [Fact]
    public void Entries_a_caller_puts_in_front_cannot_change_the_address()
    {
        var real = ContextFrom("10.1.1.1", "X-Forwarded-For", "198.51.100.40");
        var forged = ContextFrom("10.1.1.1", "X-Forwarded-For", "1.2.3.4, 5.6.7.8, 198.51.100.40");

        Assert.Equal("198.51.100.40", LoginThrottle.ClientIp(real, "X-Forwarded-For"));
        Assert.Equal("198.51.100.40", LoginThrottle.ClientIp(forged, "X-Forwarded-For"));
    }

    [Fact]
    public void When_the_header_comes_as_several_lines_the_last_line_counts()
    {
        var context = ContextFrom("10.1.1.1");
        context.Request.Headers.Append("X-Forwarded-For", "1.2.3.4");
        context.Request.Headers.Append("X-Forwarded-For", "198.51.100.40");

        Assert.Equal("198.51.100.40", LoginThrottle.ClientIp(context, "X-Forwarded-For"));
    }

    [Fact]
    public void A_list_whose_last_entry_is_not_an_address_falls_back_to_the_socket()
    {
        // Trailing junk must not let a valid-looking forged entry earlier in the list through.
        var context = ContextFrom("10.1.1.1", "X-Forwarded-For", "203.0.113.9, <script>");
        Assert.Equal("10.1.1.1", LoginThrottle.ClientIp(context, "X-Forwarded-For"));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("<script>")]
    public void A_header_that_is_not_an_ip_address_is_ignored(string value)
    {
        var context = ContextFrom("10.1.1.1", "CF-Connecting-IP", value);
        Assert.Equal("10.1.1.1", LoginThrottle.ClientIp(context, "CF-Connecting-IP"));
    }

    [Fact]
    public void A_missing_header_and_socket_address_fall_back_to_unknown()
    {
        Assert.Equal("unknown", LoginThrottle.ClientIp(ContextFrom(null), "CF-Connecting-IP"));
    }
}

/// <summary>The same app, told to read the client address from CF-Connecting-IP (as on Render).</summary>
public class ThrottledApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("LoginThrottle:ClientIpHeader", "CF-Connecting-IP");
    }
}

public class LoginThrottleApiTests : IClassFixture<ThrottledApiFactory>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private readonly ThrottledApiFactory _factory;

    public LoginThrottleApiTests(ThrottledApiFactory factory) => _factory = factory;

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

    private Task<HttpResponseMessage> LoginAsync(string clientIp, string user, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = $"{user}@example.com", password }),
        };
        request.Headers.Add("CF-Connecting-IP", clientIp);
        return _factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_get_429_with_retry_after_but_other_clients_are_unaffected()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync("198.51.100.10", "manager-a", "wrong")).StatusCode);

        var blocked = await LoginAsync("198.51.100.10", "manager-a", "wrong");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Too many", body.GetProperty("message").GetString());

        // Even the right password waits out the block ...
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await LoginAsync("198.51.100.10", "manager-a", ApiFactory.Password)).StatusCode);
        // ... but the real owner of that account, on another address, signs in normally.
        Assert.Equal(HttpStatusCode.OK,
            (await LoginAsync("198.51.100.11", "manager-a", ApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Forging_the_start_of_a_forwarded_list_does_not_dodge_the_limit()
    {
        // The proxy appended the real address (198.51.100.40); the caller invented a new one in front each time.
        for (var i = 1; i <= 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync($"203.0.113.{i}, 198.51.100.40", "forged-a", "wrong")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync("203.0.113.99, 198.51.100.40", "forged-a", "wrong")).StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_count_for_that_account()
    {
        for (var i = 0; i < 4; i++) await LoginAsync("198.51.100.20", "staff-a", "wrong");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("198.51.100.20", "staff-a", ApiFactory.Password)).StatusCode);

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync("198.51.100.20", "staff-a", "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync("198.51.100.20", "staff-a", ApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task One_client_guessing_across_many_accounts_is_stopped()
    {
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync("198.51.100.30", $"ghost{i}", "wrong")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync("198.51.100.30", "ghost99", "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await LoginAsync("198.51.100.30", "admin", ApiFactory.Password)).StatusCode);
    }
}
