
using System.Text;
using KPI.Application.Auth;
using KPI.API.Authorization;
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddResend(options => {options.ApiToken = builder.Configuration["Resend:ApiKey"]!;});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IDelayDetectionJob, DelayDetectionJob>();
builder.Services.AddScoped<IAuditService, AuditService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "https://kpi-management-two.vercel.app",
                "https://kpi-management.vercel.app"
              )
              .AllowAnyHeader()
              .AllowAnyMethod();
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
var jwtKey = builder.Configuration["Jwt:Key"]!;
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
    });

builder.Services.AddAuthorization();
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(
        builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var app = builder.Build();

// Seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors("AllowFrontend");
app.UseHangfireDashboard("/hangfire");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

RecurringJob.AddOrUpdate<DelayDetectionJob>(
    "delay-detection",
    job => job.RunAsync(),
    Cron.Daily);

app.Run();