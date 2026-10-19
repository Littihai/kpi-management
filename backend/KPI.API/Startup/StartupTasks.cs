using Hangfire;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Startup;

/// <summary>Work the app does once, after the container is built and before it takes requests.</summary>
public static class StartupTasks
{
    /// <summary>Brings the schema up to date, then seeds roles, permissions and (on a new database) the first SuperAdmin.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        var config = app.Configuration;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Bring the schema up to date before anything reads it, so a deploy never runs new code on old tables.
        // Skipped for the in-memory test database; set Database__AutoMigrate=false to migrate by hand instead.
        if (db.Database.IsRelational()
            && !string.Equals(config["Database:AutoMigrate"], "false", StringComparison.OrdinalIgnoreCase))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
                app.Logger.LogInformation("Applying {Count} pending database migration(s): {Migrations}",
                    pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
        }

        // Seed roles and keep permissions in sync with RolePermissionMap
        await DbSeeder.SeedAsync(db);

        // Public registration is closed, so a new database needs its first SuperAdmin from configuration.
        if (await DbSeeder.SeedInitialAdminAsync(
                db,
                config["Admin:Email"],
                config["Admin:Password"],
                config["Admin:FirstName"],
                config["Admin:LastName"]))
            app.Logger.LogWarning(
                "Created the first SuperAdmin {Email} from the Admin__* settings. Remove Admin__Password once you have signed in.",
                config["Admin:Email"]);
    }

    /// <summary>
    /// Uses the service-based API. The static RecurringJob.AddOrUpdate needs JobStorage.Current to be set already,
    /// which used to happen only as a side effect of mapping the Hangfire dashboard (now development-only), so
    /// production crashed on start-up with "JobStorage instance has not been initialized yet".
    /// </summary>
    public static void RegisterRecurringJobs(this WebApplication app)
    {
        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<MaintenanceJob>("maintenance", job => job.RunAsync(), Cron.Daily);
    }
}
