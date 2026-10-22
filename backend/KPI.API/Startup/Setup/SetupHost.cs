using System.Net;
using KPI.API.Controllers;
using KPI.Application.Database;
using KPI.Application.Settings;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KPI.API.Startup.Setup;

/// <summary>Why the application is waiting to be set up instead of running.</summary>
public enum SetupReason { NotConfigured, Unreachable, StartupFailed }

public sealed record SetupNeed(SetupReason Reason, string? Problem = null, string? Message = null);

/// <summary>The mutable facts the setup page and its endpoints share.</summary>
public sealed class SetupState
{
    public required SetupNeed Need { get; init; }
    public required SetupKey Key { get; init; }
    public required IConfiguration Configuration { get; init; }
    public required string ConfigFile { get; init; }
    public bool AllowRemote { get; init; }
    public bool CanWriteConfig { get; init; }

    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completed => _completed.Task;
    public void Complete() => _completed.TrySetResult();
}

/// <summary>Decides at start-up whether the application can run, or has to wait for someone to say where the database is.</summary>
public static class SetupMode
{
    /// <summary>
    /// The supervisor (and the wizard) is used for SQL Server, and whenever no database is configured at all. A configured
    /// PostgreSQL deployment (the hosted one) runs exactly as it always did.
    /// </summary>
    public static bool UsesSupervisor(IConfiguration config, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing")) return false;
        if (string.Equals(config["Setup:Enabled"], "false", StringComparison.OrdinalIgnoreCase)) return false;
        return !DatabaseProvider.IsConfigured(config) || DatabaseProvider.EngineOf(config) == DatabaseEngine.SqlServer;
    }

    /// <summary>Null when the database is configured and answers; otherwise why the setup page is needed. A database that is only slow to come up (the server just started) is waited for.</summary>
    public static async Task<SetupNeed?> CheckAsync(IConfiguration config, ILogger log, CancellationToken cancellationToken, TimeSpan? patience = null)
    {
        if (!DatabaseProvider.IsConfigured(config)) return new SetupNeed(SetupReason.NotConfigured);

        var deadline = DateTimeOffset.UtcNow + (patience ?? TimeSpan.FromSeconds(45));
        while (true)
        {
            var (ok, problem, message) = await TryConnectAsync(config, cancellationToken);
            if (ok) return null;
            // a wrong password or a missing database will not fix itself; a server that is still starting will
            if (problem is "login-failed" or "no-database" || DateTimeOffset.UtcNow >= deadline)
                return new SetupNeed(SetupReason.Unreachable, problem, message);
            log.LogWarning("The database does not answer yet ({Problem}); trying again", problem);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    public static async Task<(bool Ok, string? Problem, string? Message)> TryConnectAsync(IConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>();
            DatabaseProvider.Configure(options, config, quick: true);
            await using var db = new AppDbContext(options.Options);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await db.Database.OpenConnectionAsync(timeout.Token);
            return (true, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (false, "unreachable", "The server did not answer in time.");   // our own 12-second limit (the caller's cancellation was rethrown above)
        }
        catch (Exception ex)
        {
            var (problem, message) = DatabaseDoctor.ClassifyConnectionFailure(ex);
            return (false, problem, message);
        }
    }
}

/// <summary>A small web server that answers only the setup page and its API, until the database is configured.</summary>
public static class SetupHost
{
    public static async Task<bool> RunAsync(WebApplicationBuilder builder, SetupNeed need, ILogger log, CancellationToken stopping)
    {
        var configFile = Path.GetFullPath(DatabaseConfigFile.PathFor(builder.Configuration, builder.Environment.ContentRootPath));
        var key = SetupKey.Resolve(builder.Configuration, configFile);
        var state = new SetupState
        {
            Need = need,
            Key = key,
            Configuration = builder.Configuration,
            ConfigFile = configFile,
            AllowRemote = string.Equals(builder.Configuration["Setup:AllowRemote"], "true", StringComparison.OrdinalIgnoreCase),
            CanWriteConfig = DatabaseConfigFile.CanWrite(configFile),
        };
        builder.Services.AddSingleton(state);
        builder.Services.TryAddSingleton(TimeProvider.System);

        var app = builder.Build();
        SetupEndpoints.Map(app, state);
        app.UseFrontend();

        if (key.File is not null) log.LogWarning("SETUP: the database is not ready ({Reason}). Open the site on this server and enter the setup key from {File}", need.Reason, key.File);
        else log.LogWarning("SETUP: the database is not ready ({Reason}). The setup key for this run is {Key}", need.Reason, key.Reveal());
        if (!state.CanWriteConfig) log.LogError("SETUP: the settings file {File} cannot be written; give the service account write access to its folder", configFile);

        await app.StartAsync(stopping);
        Task? watcher = need.Reason == SetupReason.Unreachable ? WatchAsync(builder.Configuration, state, log, stopping) : null;
        try
        {
            await Task.WhenAny(state.Completed, Task.Delay(Timeout.Infinite, stopping), watcher ?? Task.Delay(Timeout.Infinite, stopping));
        }
        catch (OperationCanceledException) { /* the service is stopping */ }

        var completed = state.Completed.IsCompleted;
        await app.StopAsync(CancellationToken.None);
        await app.DisposeAsync();
        if (completed) key.Discard();
        return completed;
    }

    /// <summary>While the database is configured but not answering, keep trying: when it comes back, carry on without anyone doing anything.</summary>
    private static async Task WatchAsync(IConfiguration config, SetupState state, ILogger log, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested && !state.Completed.IsCompleted)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stopping);
            var (ok, _, _) = await SetupMode.TryConnectAsync(config, stopping);
            if (ok) { log.LogInformation("The database answers again; starting"); state.Complete(); return; }
        }
    }
}

/// <summary>The setup API. Every call except the status needs the setup key, and (unless allowed) must come from the server itself.</summary>
public static class SetupEndpoints
{
    public static void Map(WebApplication app, SetupState state)
    {
        var json = new Func<object, int, IResult>((body, status) => Results.Json(body, statusCode: status));

        // Read-only answers may be read from another origin (the desktop app, another machine's browser) so they can say 'waiting for setup'.
        app.Use((context, next) =>
        {
            var path = context.Request.Path;
            if (HttpMethods.IsGet(context.Request.Method)
                && (path.StartsWithSegments("/api/setup/status") || path.StartsWithSegments("/health") || path.StartsWithSegments("/api/client-config")))
                context.Response.Headers.AccessControlAllowOrigin = "*";
            return next();
        });

        app.MapGet("/health", () => Results.Json(new { status = "setup", reason = state.Need.Reason.ToString() }));
        app.MapGet("/health/ready", () => Results.Json(new { status = "setup" }, statusCode: 503));
        app.MapGet("/api/client-config", (IConfiguration config) => Results.Json(new
        {
            minDesktopVersion = ClientConfigController.NormalizeVersion(config["Client:MinDesktopVersion"]) ?? ClientConfigController.DefaultMinimum,
            latestDesktopVersion = ClientConfigController.NormalizeVersion(config["Client:LatestDesktopVersion"]),
            companyName = ClientConfigController.CompanyName(null, config),   // no database to ask yet: only what the installer was given
        }));

        // Anyone who can reach the port may ask whether setup is needed; only someone on the server (or where the administrator allowed it) is told where things are kept.
        app.MapGet("/api/setup/status", (HttpContext http) =>
        {
            var trusted = IsTrusted(http, state);
            return Results.Json(new
            {
                mode = "setup",
                reason = state.Need.Reason.ToString(),
                problem = state.Need.Problem,
                engine = "SqlServer",
                remoteAllowed = state.AllowRemote,
                canWriteConfig = state.CanWriteConfig,
                jwtConfigured = !string.IsNullOrEmpty(state.Configuration["Jwt:Key"]),
                keyFile = trusted ? state.Key.File : null,
                current = trusted && DatabaseProvider.IsConfigured(state.Configuration) && !string.IsNullOrWhiteSpace(state.Configuration["Database:Server"])
                    ? new
                    {
                        server = state.Configuration["Database:Server"],
                        database = state.Configuration["Database:Name"],
                        auth = state.Configuration["Database:Auth"],
                        user = state.Configuration["Database:User"],
                        trust = !string.Equals(state.Configuration["Database:TrustServerCertificate"], "false", StringComparison.OrdinalIgnoreCase),
                    }
                    : null,
            });
        });

        app.MapPost("/api/setup/test", async (HttpContext http, SetupConnection connection, CancellationToken cancellationToken) =>
        {
            if (Guard(http, state) is { } refused) return refused;
            if (connection.Validate() is { } invalid) return json(new { error = "invalid", message = invalid }, 400);
            await using var db = Context(connection.ConnectionString());
            var report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            return json(new
            {
                report,
                startupError = state.Need.Reason == SetupReason.StartupFailed ? state.Need.Message : null,
                jwtConfigured = !string.IsNullOrEmpty(state.Configuration["Jwt:Key"]),
            }, 200);
        });

        app.MapPost("/api/setup/apply", async (HttpContext http, SetupApplyRequest request, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        {
            if (Guard(http, state) is { } refused) return refused;
            var connection = request.Connection;
            if (connection is null || connection.Validate() is { } invalid) return json(new { error = "invalid", message = connection?.Validate() ?? "กรอกข้อมูลการเชื่อมต่อ" }, 400);
            if (!state.CanWriteConfig) return json(new { error = "cannot-write", message = $"เขียนไฟล์ตั้งค่า {state.ConfigFile} ไม่ได้ ให้สิทธิ์เขียนโฟลเดอร์นี้แก่บัญชีที่รัน service" }, 500);
            if (!connection.Windows && string.IsNullOrEmpty(state.Configuration["Jwt:Key"]))
                return json(new { error = "jwt-missing", message = "ยังไม่ได้ตั้งค่า Jwt__Key ของเซิร์ฟเวอร์ ตั้งค่านี้ก่อน (ใช้เข้ารหัสรหัสผ่านฐานข้อมูลที่บันทึก)" }, 400);

            // the administrator is checked before the database is touched: a form that will be refused must change nothing
            if (request.Admin is { } asked && !string.IsNullOrWhiteSpace(asked.Email) && InvalidAdmin(asked) is { } wrong)
                return json(new { error = "admin", message = wrong }, 400);

            var connectionString = connection.ConnectionString();
            await using var db = Context(connectionString);
            var report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            if (!report.Connected) return json(new { error = "not-connected", report }, 400);

            var done = new List<string>();
            if (request.Repair is { } repair)
            {
                var result = await DatabaseDoctor.RepairAsync(db, repair, cancellationToken);
                done.AddRange(result.Done);
                report = result.Report;
            }

            object? admin = null;
            if (request.Admin is { } wanted && !string.IsNullOrWhiteSpace(wanted.Email) && report.Seed is not { HasActiveAdmin: true })
            {
                var (code, message) = await CreateAdminAsync(connectionString, state.Configuration, wanted, cancellationToken);
                admin = new { created = code == 0, code, message };
                if (code is 3 or 2) return json(new { error = "admin", message, report }, 400);   // refused (e-mail or password): nothing saved yet
                report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            }

            string? protectedPassword = connection.Windows ? null : DatabaseConfigFile.Protect(state.Configuration, connection.Password!);
            DatabaseConfigFile.Write(state.ConfigFile, connection, protectedPassword);
            loggers.CreateLogger("Setup").LogWarning("SETUP: the database {Database} on {Server} was saved to {File}; starting the application", connection.Database, connection.Server, state.ConfigFile);

            // answer first, then let the supervisor swap this page for the application
            _ = Task.Delay(1500, CancellationToken.None).ContinueWith(_ => state.Complete(), TaskScheduler.Default);
            return json(new { saved = true, done, admin, report }, 200);
        });

        // anything else under /api: the application is not running yet
        app.Map("/api/{**rest}", () => Results.Json(new { setupRequired = true }, statusCode: 503));
    }

    /// <summary>What is wrong with the administrator that can be seen without the database (the password rules are in its settings and are applied when the account is made).</summary>
    private static string? InvalidAdmin(SetupAdmin admin)
    {
        var email = admin.Email!.Trim();
        if (!email.Contains('@') || email.Contains(' ') || email.Length > 254) return "อีเมลผู้ดูแลระบบไม่ถูกต้อง";
        if (string.IsNullOrWhiteSpace(admin.FirstName)) return "กรอกชื่อผู้ดูแลระบบ";
        if (string.IsNullOrEmpty(admin.Password)) return "กรอกรหัสผ่านผู้ดูแลระบบ";
        return null;
    }

    private static AppDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseProvider.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:DefaultConnection"] = connectionString,
        }).Build(), quick: true);   // the person is waiting for an answer
        return new AppDbContext(options.Options);
    }

    private static bool IsTrusted(HttpContext http, SetupState state) =>
        state.AllowRemote || IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None);

    private static IResult? Guard(HttpContext http, SetupState state)
    {
        if (!IsTrusted(http, state))
            return Results.Json(new { error = "local-only", message = "หน้านี้เปิดได้จากตัวเซิร์ฟเวอร์เท่านั้น (เปิดเว็บบนเครื่องเซิร์ฟเวอร์ด้วย http://localhost:พอร์ต)" }, statusCode: 403);
        if (state.Key.BlockedFor() is { } wait)
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Results.Json(new { error = "too-many-attempts", message = "ใส่รหัสผิดหลายครั้ง รอสักครู่แล้วลองใหม่", retryAfterSeconds = (int)wait.TotalSeconds }, statusCode: 429);
        }
        if (!state.Key.Matches(http.Request.Headers["X-Setup-Key"].ToString()))
        {
            state.Key.RecordFailure();
            return Results.Json(new { error = "bad-key", message = "รหัสตั้งค่าไม่ถูกต้อง" }, statusCode: 401);
        }
        state.Key.RecordSuccess();
        return null;
    }

    /// <summary>The first administrator, made with the same rules and code as the create-admin command, on the database that was just chosen.</summary>
    private static async Task<(int Code, string Message)> CreateAdminAsync(string connectionString, IConfiguration config, SetupAdmin admin, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(config);
        services.AddDbContext<AppDbContext>(o => DatabaseProvider.Configure(o, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:DefaultConnection"] = connectionString,
        }).Build()));
        services.AddSingleton<ISecretProtector>(new SecretProtector(string.IsNullOrEmpty(config["Jwt:Key"]) ? "setup-only-key-" + Guid.NewGuid().ToString("N") : config["Jwt:Key"]!));
        services.AddSingleton<ISystemSettings, SystemSettingsService>();
        services.AddScoped<KPI.Application.Auth.IPasswordPolicy, PasswordPolicyService>();
        services.AddScoped<KPI.Application.Auth.IPasswordService, PasswordService>();
        services.AddSingleton<KPI.Application.Audit.IAuditContext>(KPI.Application.Audit.NullAuditContext.Instance);
        services.AddScoped<KPI.Application.Auth.IAuthLog, AuthLogService>();
        await using var provider = services.BuildServiceProvider();

        var output = new StringWriter();
        var code = await AdminCommand.RunAsync(provider, name => name switch
        {
            "HELPDESK_ADMIN_EMAIL" => admin.Email,
            "HELPDESK_ADMIN_PASSWORD" => admin.Password,
            "HELPDESK_ADMIN_FIRST_NAME" => admin.FirstName,
            "HELPDESK_ADMIN_LAST_NAME" => admin.LastName,
            _ => null,
        }, output);
        return (code, output.ToString().Trim());
    }
}

public sealed record SetupApplyRequest(SetupConnection? Connection, RepairRequest? Repair, SetupAdmin? Admin);
