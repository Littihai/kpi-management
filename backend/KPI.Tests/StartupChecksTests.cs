using System.Net;
using KPI.API.Health;
using KPI.API.Security;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace KPI.Tests;

public class JwtKeyValidatorTests
{
    [Theory]
    [InlineData("integration-test-signing-key-0123456789-abcdef")]
    [InlineData("q7Zr2Lx9PvTn4Ke8WbYc5Hd3GmSa6JfU")] // exactly 32 characters
    public void Accepts_a_long_random_key(string key) => Assert.Null(JwtKeyValidator.Problem(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_key(string? key) => Assert.Contains("not set", JwtKeyValidator.Problem(key));

    [Fact]
    public void Rejects_a_key_shorter_than_32_bytes()
    {
        Assert.Contains("at least 32", JwtKeyValidator.Problem("q7Zr2Lx9PvTn4Ke8WbYc5Hd3GmSa6Jf")); // 31
    }

    [Theory]
    [InlineData("your-super-secret-key-minimum-32-characters!!")] // the value once committed in appsettings.json
    [InlineData("YOUR-SUPER-SECRET-KEY-MINIMUM-32-CHARACTERS!!")]
    [InlineData("<random secret, 32+ characters>")]               // appsettings.Example.json
    [InlineData("please-changeme-before-going-live-0123456789")]
    public void Rejects_the_placeholders_that_are_published_in_the_repository(string key)
    {
        Assert.NotNull(JwtKeyValidator.Problem(key));
    }

    [Fact]
    public void Rejects_a_repetitive_key()
    {
        Assert.Contains("repetitive", JwtKeyValidator.Problem(new string('a', 64)));
        Assert.Contains("repetitive", JwtKeyValidator.Problem("abababababababababababababababab"));
    }

    [Fact]
    public void Never_echoes_the_key_back()
    {
        const string key = "your-super-secret-key-minimum-32-characters!!";
        Assert.DoesNotContain(key, JwtKeyValidator.Problem(key));
    }
}

/// <summary>The app started with the placeholder key that used to sit in appsettings.json.</summary>
public class PlaceholderKeyApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jwt:Key", "your-super-secret-key-minimum-32-characters!!");
    }
}

public class StartupChecksTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StartupChecksTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void The_app_refuses_to_start_with_a_placeholder_signing_key()
    {
        using var factory = new PlaceholderKeyApiFactory();

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var messages = string.Join(" | ", Chain(ex).Select(e => e.Message));
        Assert.Contains("Jwt:Key", messages);
        Assert.DoesNotContain("your-super-secret-key-minimum-32-characters!!", messages);
    }

    private static IEnumerable<Exception> Chain(Exception? e)
    {
        for (; e is not null; e = e.InnerException) yield return e;
    }

    // ───────────── health ─────────────

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_public_and_report_healthy(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_when_the_database_cannot_be_used_and_hides_the_reason()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.DisposeAsync(); // any use now throws, like a dropped connection would

        var result = await new DatabaseHealthCheck(db, NullLogger<DatabaseHealthCheck>.Instance)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("Disposed", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_database_check_is_healthy_for_a_reachable_database()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var result = await new DatabaseHealthCheck(db, NullLogger<DatabaseHealthCheck>.Instance)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
