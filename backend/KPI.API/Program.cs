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

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);
// Integration tests run the real pipeline against an in-memory database and without Hangfire.
var runBackgroundJobs = !builder.Environment.IsEnvironment("Testing");

builder.Services.AddControllers();
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
builder.Services.AddScoped<KpiEventPublisher>();

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
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set the Jwt__Key environment variable (see appsettings.Example.json).");

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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

// Seed roles and keep permissions in sync with RolePermissionMap
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

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

if (runBackgroundJobs)
{
    RecurringJob.AddOrUpdate<DelayDetectionJob>(
        "delay-detection",
        job => job.RunAsync(),
        Cron.Daily);
}

app.Run();

// Lets the integration-test project start the app with WebApplicationFactory<Program>.
public partial class Program { }
