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
    /// Serves the web app from the API itself when it sits next to it (a "wwwroot" folder with an index.html), so a
    /// server needs no IIS or nginx: one process answers the pages, /api and /hubs. Any other path gets index.html, which
    /// is what lets the app's own routes (/issues/123) survive a refresh. Does nothing when there is no wwwroot.
    /// </summary>
    public static void UseFrontend(this WebApplication app)
    {
        var webRoot = app.Environment.WebRootPath;
        var index = webRoot is null ? null : Path.Combine(webRoot, "index.html");
        if (index is null || !File.Exists(index)) return;

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                // file names under /assets carry a content hash, so they can be kept forever
                if (context.Context.Request.Path.StartsWithSegments("/assets"))
                    context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            },
        });
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/health")
                || Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        });
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
