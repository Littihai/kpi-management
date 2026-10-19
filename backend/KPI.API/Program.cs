using KPI.API.Audit;
using KPI.API.Errors;
using KPI.API.Health;
using KPI.API.Realtime;
using KPI.API.Startup;
using KPI.Infrastructure.Data;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

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

var app = builder.Build();

await app.MigrateAndSeedAsync();

app.UseResponseCompression();
// Outside the exception handler so the logged status is the one the client actually receives.
app.UseMiddleware<RequestLogMiddleware>();
app.UseExceptionHandler();

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

app.Run();

// Lets the integration-test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
