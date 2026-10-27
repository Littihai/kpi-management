using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.SqlServer;
using KPI.Infrastructure.Data;
using KPI.API.Audit;
using KPI.API.Authorization;
using KPI.API.Realtime;
using KPI.API.Security;
using KPI.Application.Admin;
using KPI.Application.Settings;
using KPI.Application.Audit;
using KPI.Application.Auth;
using KPI.Application.Issues;
using KPI.Application.Jobs;
using KPI.Application.Notifications;
using KPI.Domain.Constants;
using KPI.Domain.Interfaces;
using KPI.Domain.Issues;
using KPI.Infrastructure.Jobs;
using KPI.Infrastructure.Repositories;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace KPI.API.Startup;

/// <summary>Service registration, grouped by topic so Program.cs reads as the outline of the app.</summary>
public static class ServiceRegistration
{
    /// <summary>Sign-in, e-mail, audit and the real-time update hub.</summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

        services.AddScoped<IMailTransport, SmtpTransport>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IEmailDiagnostics, SmtpDiagnostics>();
        services.AddSingleton<ISecretProtector>(_ => new SecretProtector(config["Jwt:Key"]!));
        services.AddScoped<IAuditService, AuditService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasswordPolicy, PasswordPolicyService>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IAuthLog, AuthLogService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrgAdminService, OrgAdminService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();
        return services;
    }

    /// <summary>Problem reports: the service, the SLA targets and the throttle on the public form.</summary>
    public static IServiceCollection AddIssueTracking(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IIssueService, IssueService>();
        services.AddScoped<IssueEventPublisher>();
        services.AddScoped<IMaintenanceJob, MaintenanceJob>();
        // SLA targets, limits and retention come from the settings screen (see SettingCatalog), not from configuration.
        services.AddSingleton<ISystemSettings, SystemSettingsService>();
        services.AddScoped<ISettingsAdminService, SettingsAdminService>();
        services.Configure<PublicSubmitOptions>(config.GetSection("PublicIssues"));
        services.AddSingleton<PublicSubmitThrottle>();
        services.AddSingleton<PublicIntakeLedger>();
        services.AddScoped<PublicIntakeBudget>();
        return services;
    }

    /// <summary>Who did what and from where, plus a background-written log of writes, refusals and sign-ins.</summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContext, HttpAuditContext>();
        services.AddScoped<IOverviewService, OverviewService>();
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<IPresenceTracker>(sp => sp.GetRequiredService<PresenceTracker>());
        services.AddSingleton<RequestLogQueue>();
        services.AddSingleton(sp => AnonymousRequestLogGate.From(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<RequestLogWriter>();
        return services;
    }

    /// <summary>Failed sign-ins are counted in memory, so the throttle must live as long as the process.</summary>
    public static IServiceCollection AddLoginThrottle(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LoginThrottleOptions>(config.GetSection("LoginThrottle"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LoginThrottle>();
        return services;
    }

    /// <summary>
    /// SignalR needs credentials, so origins must be listed explicitly. Extra origins, e.g. a Vercel preview URL,
    /// can be added with Cors__Origins__0, Cors__Origins__1, ...
    /// </summary>
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration config)
    {
        var allowedOrigins = new[]
            {
                "http://localhost:5173",
                "https://kpi-management-two.vercel.app",
                "https://kpi-management.vercel.app",
                "app://local"
            }
            .Concat(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
            .ToArray();

        return services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
    }

    public const string CorsPolicy = "AllowFrontend";

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var jwtKey = config["Jwt:Key"];
        if (JwtKeyValidator.Problem(jwtKey) is { } jwtKeyProblem)
            throw new InvalidOperationException(
                $"Jwt:Key cannot be used: {jwtKeyProblem} Set the Jwt__Key environment variable to a random secret " +
                "of 32+ characters (see appsettings.Example.json).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
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
        return services;
    }

    /// <summary>One policy per permission: [Authorize(Policy = Permissions.Kpi.Approve)]</summary>
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in Permissions.All)
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
        });
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        return services;
    }

    /// <summary>Hangfire, in the same database as the data (PostgreSQL or SQL Server), or in memory for a test that starts the whole app.</summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration config)
    {
        services.AddHangfire(hangfire =>
        {
            if (string.Equals(config["Jobs:Storage"], "InMemory", StringComparison.OrdinalIgnoreCase))
                hangfire.UseInMemoryStorage();
            else if (DatabaseProvider.EngineOf(config) == DatabaseEngine.SqlServer)
                hangfire.UseSqlServerStorage(DatabaseProvider.ConnectionString(config));
            else
                hangfire.UsePostgreSqlStorage(DatabaseProvider.ConnectionString(config));
        });
        services.AddHangfireServer();
        return services;
    }
}
