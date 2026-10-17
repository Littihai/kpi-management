using System.Text;
using KPI.Application.Auth;
using KPI.API.Authorization;
using KPI.API.Errors;
using KPI.API.Realtime;
using KPI.Domain.Constants;
using KPI.Domain.Interfaces;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using KPI.Application.Kpis;
using KPI.Application.Dashboard;
using KPI.Application.Export;
using KPI.Application.Notifications;
using Resend;
using Hangfire;
using Hangfire.PostgreSql;
using KPI.Application.Jobs;
using KPI.Infrastructure.Jobs;
using KPI.Application.Audit;
using KPI.API.Health;
using KPI.API.Security;
using KPI.API.Audit;
using KPI.Application.Admin;
using KPI.Application.Issues;
using KPI.Domain.Issues;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);
// Integration tests run the real pipeline against an in-memory database and without Hangfire.
var runBackgroundJobs = !builder.Environment.IsEnvironment("Testing");

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new FlexibleEnumConverterFactory()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSignalR();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddResend(options => {options.ApiToken = builder.Configuration["Resend:ApiKey"]!;});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IDelayDetectionJob, DelayDetectionJob>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddScoped<KpiEmailNotifier>();
builder.Services.AddScoped<KpiEventPublisher>();

// Issues (problem reports)
builder.Services.AddScoped<IIssueService, IssueService>();
builder.Services.AddScoped<IssueEventPublisher>();
builder.Services.AddScoped<IMaintenanceJob, MaintenanceJob>();
builder.Services.AddSingleton(new IssueSlaPolicy(
    TimeSpan.FromHours(builder.Configuration.GetValue("Issues:FirstResponseHours", 2.0)),
    TimeSpan.FromHours(builder.Configuration.GetValue("Issues:ResolveHours:Critical", 4.0)),
    TimeSpan.FromHours(builder.Configuration.GetValue("Issues:ResolveHours:High", 24.0)),
    TimeSpan.FromHours(builder.Configuration.GetValue("Issues:ResolveHours:Medium", 72.0)),
    TimeSpan.FromHours(builder.Configuration.GetValue("Issues:ResolveHours:Low", 168.0))));
builder.Services.Configure<PublicSubmitOptions>(builder.Configuration.GetSection("PublicIssues"));
builder.Services.AddSingleton<PublicSubmitThrottle>();

// Audit: who did what and from where, plus a background-written log of writes, refusals and sign-ins.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditContext, HttpAuditContext>();
builder.Services.AddScoped<IOverviewService, OverviewService>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<IPresenceTracker>(sp => sp.GetRequiredService<PresenceTracker>());
builder.Services.AddSingleton<RequestLogQueue>();
builder.Services.AddHostedService<RequestLogWriter>();

// Smaller JSON responses over the wire (the API and the database are often far apart from the user).
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// /health answers "is the process up"; /health/ready also checks the database.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Failed sign-ins are counted in memory, so the throttle must live as long as the process.
builder.Services.Configure<LoginThrottleOptions>(builder.Configuration.GetSection("LoginThrottle"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginThrottle>();

// CORS (SignalR needs credentials, so origins must be listed explicitly).
// Extra origins, e.g. a Vercel preview URL, can be added with Cors__Origins__0, Cors__Origins__1, ...
var allowedOrigins = new[]
    {
        "http://localhost:5173",
        "https://kpi-management-two.vercel.app",
        "https://kpi-management.vercel.app"
    }
    .Concat(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddScoped<IKpiRepository, KpiRepository>();
builder.Services.AddScoped<IKpiService, KpiService>();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
if (JwtKeyValidator.Problem(jwtKey) is { } jwtKeyProblem)
    throw new InvalidOperationException(
        $"Jwt:Key cannot be used: {jwtKeyProblem} Set the Jwt__Key environment variable to a random secret " +
        "of 32+ characters (see appsettings.Example.json).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!))
        };

        // Browsers cannot set an Authorization header on a WebSocket, so SignalR sends the
        // token as ?access_token=... on hub URLs only.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// One policy per permission: [Authorize(Policy = Permissions.Kpi.Approve)]
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
});

if (runBackgroundJobs)
{
    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(
            builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddHangfireServer();
}
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Bring the schema up to date before anything reads it, so a deploy never runs new code on old tables.
    // Skipped for the in-memory test database; set Database__AutoMigrate=false to migrate by hand instead.
    if (db.Database.IsRelational()
        && !string.Equals(builder.Configuration["Database:AutoMigrate"], "false", StringComparison.OrdinalIgnoreCase))
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
            builder.Configuration["Admin:Email"],
            builder.Configuration["Admin:Password"],
            builder.Configuration["Admin:FirstName"],
            builder.Configuration["Admin:LastName"]))
        app.Logger.LogWarning(
            "Created the first SuperAdmin {Email} from the Admin__* settings. Remove Admin__Password once you have signed in.",
            builder.Configuration["Admin:Email"]);
}

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

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<KpiHub>("/hubs/kpi");
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (runBackgroundJobs)
{
    RecurringJob.AddOrUpdate<DelayDetectionJob>(
        "delay-detection",
        job => job.RunAsync(),
        Cron.Daily);

    RecurringJob.AddOrUpdate<MaintenanceJob>(
        "maintenance",
        job => job.RunAsync(),
        Cron.Daily);
}

app.Run();

// Lets the integration-test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
