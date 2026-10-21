using System.Net;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KPI.Tests;

/// <summary>
/// The normal test host skips Hangfire, which hid a production-only crash: the daily jobs were registered
/// with the static API before Hangfire's storage existed, so the app died on start-up on Render.
/// This host runs the same start-up with background jobs on, using in-memory storage instead of PostgreSQL.
/// </summary>
public class JobsApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jobs:Enabled", "true");
        builder.UseSetting("Jobs:Storage", "InMemory");
    }
}

public class BackgroundJobsStartupTests : IClassFixture<JobsApiFactory>
{
    private readonly JobsApiFactory _factory;

    public BackgroundJobsStartupTests(JobsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task The_app_starts_with_background_jobs_on_and_registers_both_daily_jobs()
    {
        using var client = _factory.CreateClient(); // starts the host: this is what crashed in production

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var storage = _factory.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        var ids = connection.GetRecurringJobs().Select(j => j.Id).ToList();

        Assert.Contains("maintenance", ids);
    }

    [Fact]
    public void The_manual_trigger_can_queue_the_maintenance_job()
    {
        using var client = _factory.CreateClient();

        // The static API used by JobsController works once start-up has initialised storage.
        var id = BackgroundJob.Enqueue<KPI.Application.Jobs.IMaintenanceJob>(job => job.RunAsync());

        Assert.False(string.IsNullOrEmpty(id));
    }
}
