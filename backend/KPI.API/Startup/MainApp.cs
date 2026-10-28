using KPI.API.Audit;
using KPI.API.Errors;
using KPI.API.Health;
using KPI.API.Realtime;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using KPI.Infrastructure.Data;

namespace KPI.API.Startup;

/// <summary>The application itself: its services and its request pipeline. Built by Program, or by the supervisor each time it (re)starts.</summary>
public static class MainApp
{
    /// <summary>Registers everything the application needs. Returns whether the background jobs (Hangfire) are switched on.</summary>
    public static bool ConfigureServices(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;

        // Integration tests run the real pipeline against an in-memory database and without Hangfire.
        // (A test can switch jobs back on with Jobs:Enabled=true and in-memory storage, to exercise start-up.)
        var runBackgroundJobs = !builder.Environment.IsEnvironment("Testing")
            || string.Equals(config["Jobs:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        builder.Services.AddControllers().AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(new FlexibleEnumConverterFactory()));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();

        builder.Services.AddDbContext<AppDbContext>((services, options) =>
        {
            DatabaseProvider.Configure(options, config);
            options.AddInterceptors(services.GetRequiredService<AccessChangeInterceptor>());
        });

        builder.Services.AddCoreServices(config);
        builder.Services.AddIssueTracking(config);
        builder.Services.AddAuditing();
        builder.Services.AddLoginThrottle(config);
        builder.Services.AddFrontendCors(config);
        builder.Services.AddJwtAuthentication(config);
        builder.Services.AddPermissionPolicies();
        if (runBackgroundJobs) builder.Services.AddBackgroundJobs(config);

        // Smaller JSON responses over the wire (the API and the database are often far apart from the user).
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        // /health answers "is the process up"; /health/ready also checks the database.
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        return runBackgroundJobs;
    }

    /// <summary>Builds the application, brings the database up to date and sets up the request pipeline. Does not start it.</summary>
    public static async Task<WebApplication> BuildAsync(WebApplicationBuilder builder)
    {
        var runBackgroundJobs = ConfigureServices(builder);
        var app = builder.Build();

        await app.MigrateAndSeedAsync();

        app.UseResponseCompression();
        // Outside the exception handler so the logged status is the one the client actually receives.
        app.UseMiddleware<RequestLogMiddleware>();
        app.UseExceptionHandler();
        app.UseFrontend();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            // The Hangfire dashboard has no per-user authorization, so it is not exposed in production.
            app.UseHangfireDashboard("/hangfire");
        }

        app.UseCors(ServiceRegistration.CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<KpiHub>("/hubs/kpi");
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        if (runBackgroundJobs) app.RegisterRecurringJobs();
        return app;
    }
}
