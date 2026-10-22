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
    /// <summary>
    /// Proves the person may change the server's settings. Null when nobody needs to prove it: on the server itself there is nothing to
    /// prove (whoever can open a browser there can reach everything anyway), so a key exists only when one was configured (Setup:Key)
    /// or when changes from other machines were allowed (Setup:AllowRemote).
    /// </summary>
    public SetupKey? Key { get; init; }
    /// <summary>The key is asked for on the server itself too (it was set in the configuration on purpose).</summary>
    public bool KeyForLocal { get; init; }
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
        var explicitKey = builder.Configuration["Setup:Key"] is { Length: >= 8 };
        var allowRemote = string.Equals(builder.Configuration["Setup:AllowRemote"], "true", StringComparison.OrdinalIgnoreCase);
        var key = explicitKey || allowRemote ? SetupKey.Resolve(builder.Configuration, configFile) : null;
        var state = new SetupState
        {
            Need = need,
            Key = key,
            KeyForLocal = explicitKey,
            Configuration = builder.Configuration,
            ConfigFile = configFile,
            AllowRemote = allowRemote,
            CanWriteConfig = DatabaseConfigFile.CanWrite(configFile),
        };
        builder.Services.AddSingleton(state);
        builder.Services.TryAddSingleton(TimeProvider.System);

        var app = builder.Build();
        SetupEndpoints.Map(app, state);
        app.UseFrontend();

        if (key is null) log.LogWarning("SETUP: the database is not ready ({Reason}). Open the site on this server and connect it to SQL Server", need.Reason);
        else if (key.File is not null) log.LogWarning("SETUP: the database is not ready ({Reason}). Open the site and enter the setup key from {File}", need.Reason, key.File);
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
        if (completed) key?.Discard();
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
                keyRequired = state.Key is not null && (!IsLoopback(http) || state.KeyForLocal),
                keyFile = trusted ? state.Key?.File : null,
                machineLogin = SqlServerProvisioner.DefaultLoginName(),
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

        // Connect to the server (as sa, or the Windows account) and say what is there and what pressing "start" would do. Changes nothing.
        app.MapPost("/api/setup/test", async (HttpContext http, SetupTestRequest request, CancellationToken cancellationToken) =>
        {
            if (Guard(http, state) is { } refused) return refused;
            var connection = request.Connection;
            if (connection is null || connection.Validate() is { } invalid) return json(new { error = "invalid", message = connection?.Validate() ?? "กรอกข้อมูลการเชื่อมต่อ" }, 400);

            var probe = await SqlServerProvisioner.ProbeAsync(connection.Access(), connection.Database!.Trim(), cancellationToken);
            DatabaseReport? report = null;
            SetupPlan? plan = null;
            if (probe.Connected)
            {
                plan = Plan(connection, request.AppAccess, probe);
                if (probe is { DatabaseExists: true, CanOpenDatabase: true })
                {
                    await using var db = Context(connection.ConnectionString());
                    report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
                }
            }
            return json(new
            {
                server = probe,
                report,
                plan,
                startupError = state.Need.Reason == SetupReason.StartupFailed ? state.Need.Message : null,
                jwtConfigured = !string.IsNullOrEmpty(state.Configuration["Jwt:Key"]),
            }, 200);
        });

        // Do it: create the database if it is not there (once, however many machines are set up against this server), give this machine
        // an account of its own, create what is missing in the database, make the first administrator, remember the answer and start.
        app.MapPost("/api/setup/apply", async (HttpContext http, SetupApplyRequest request, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        {
            if (Guard(http, state) is { } refused) return refused;
            var connection = request.Connection;
            if (connection is null || connection.Validate() is { } invalid) return json(new { error = "invalid", message = connection?.Validate() ?? "กรอกข้อมูลการเชื่อมต่อ" }, 400);
            if (!state.CanWriteConfig) return json(new { error = "cannot-write", message = $"เขียนไฟล์ตั้งค่า {state.ConfigFile} ไม่ได้ ให้สิทธิ์เขียนโฟลเดอร์นี้แก่บัญชีที่รัน service" }, 500);
            if (!connection.Windows && string.IsNullOrEmpty(state.Configuration["Jwt:Key"]))
                return json(new { error = "jwt-missing", message = "ยังไม่ได้ตั้งค่า Jwt__Key ของเซิร์ฟเวอร์ ตั้งค่านี้ก่อน (ใช้เข้ารหัสรหัสผ่านฐานข้อมูลที่บันทึก)" }, 400);

            // the administrator is checked before the server is touched: a form that will be refused must change nothing
            if (request.Admin is { } asked && !string.IsNullOrWhiteSpace(asked.Email) && InvalidAdmin(asked) is { } wrong)
                return json(new { error = "admin", message = wrong }, 400);

            var log = loggers.CreateLogger("Setup");
            var database = connection.Database!.Trim();
            var access = connection.Access();
            var probe = await SqlServerProvisioner.ProbeAsync(access, database, cancellationToken);
            if (!probe.Connected) return json(new { error = "not-connected", server = probe }, 400);
            var plan = Plan(connection, request.AppAccess, probe);
            if (plan.Blocker is { } blocker) return json(new { error = blocker, message = BlockerText(blocker, database), server = probe, plan }, 400);

            var done = new List<string>();
            string? appUser = connection.User, appPassword = connection.Password;
            try
            {
                if (plan.CreateDatabase)
                {
                    if (await SqlServerProvisioner.EnsureDatabaseAsync(access, database, cancellationToken)) { done.Add("create-database"); log.LogWarning("SETUP: created the database {Database} on {Server}", database, connection.Server); }
                    else log.LogInformation("SETUP: the database {Database} was created by someone else a moment ago; using it", database);
                }
                if (plan.Access == "dedicated")
                {
                    var login = await SqlServerProvisioner.EnsureAppLoginAsync(access, database, plan.Login!, cancellationToken);
                    appUser = login.Login;
                    appPassword = login.Password;
                    done.Add(login.Created ? "create-login" : "reset-login");
                    log.LogWarning("SETUP: {Action} the SQL login {Login} for this machine (db_owner of {Database} only)", login.Created ? "created" : "reset the password of", login.Login, database);
                }
            }
            catch (ProvisioningException ex)
            {
                return json(new { error = ex.Code, message = ex.Message, done }, 400);
            }

            // from here on the application's own account is used, which proves it works before anything is saved
            var application = new SetupConnection(connection.Server, database, connection.Windows ? "windows" : "sql", appUser, appPassword, connection.Trust);
            var connectionString = application.ConnectionString();
            await using var db = Context(connectionString);
            var report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            if (!report.Connected) return json(new { error = "app-login-failed", message = "สร้างแล้ว แต่เชื่อมต่อด้วยบัญชีของโปรแกรมไม่ได้", report, done }, 400);

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
                if (code is 3 or 2) return json(new { error = "admin", message, report, done }, 400);   // refused (e-mail or password): nothing saved yet
                report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            }

            string? protectedPassword = application.Windows ? null : DatabaseConfigFile.Protect(state.Configuration, appPassword!);
            DatabaseConfigFile.Write(state.ConfigFile, application, protectedPassword);
            log.LogWarning("SETUP: the database {Database} on {Server} was saved to {File}; starting the application", database, connection.Server, state.ConfigFile);

            // answer first, then let the supervisor swap this page for the application
            _ = Task.Delay(1500, CancellationToken.None).ContinueWith(_ => state.Complete(), TaskScheduler.Default);
            return json(new { saved = true, createdDatabase = done.Contains("create-database"), access = plan.Access, login = plan.Login, done, admin, report }, 200);
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

    private static bool IsLoopback(HttpContext http) => IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None);

    private static bool IsTrusted(HttpContext http, SetupState state) => state.AllowRemote || IsLoopback(http);

    private static IResult? Guard(HttpContext http, SetupState state)
    {
        if (!IsTrusted(http, state))
            return Results.Json(new { error = "local-only", message = "หน้านี้เปิดได้จากตัวเซิร์ฟเวอร์เท่านั้น (เปิดเว็บบนเครื่องเซิร์ฟเวอร์ด้วย http://localhost:พอร์ต)" }, statusCode: 403);
        // On the server itself there is nothing to prove unless a key was set on purpose; from anywhere else the key is needed.
        if (state.Key is not { } key || (IsLoopback(http) && !state.KeyForLocal)) return null;
        if (key.BlockedFor() is { } wait)
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return Results.Json(new { error = "too-many-attempts", message = "ใส่รหัสผิดหลายครั้ง รอสักครู่แล้วลองใหม่", retryAfterSeconds = (int)wait.TotalSeconds }, statusCode: 429);
        }
        if (!key.Matches(http.Request.Headers["X-Setup-Key"].ToString()))
        {
            key.RecordFailure();
            return Results.Json(new { error = "bad-key", message = "รหัสตั้งค่าไม่ถูกต้อง" }, statusCode: 401);
        }
        key.RecordSuccess();
        return null;
    }

    /// <summary>What pressing "start" would do on this server, and what stops it.</summary>
    private static SetupPlan Plan(SetupConnection connection, SetupAppAccess? requested, ServerProbe probe)
    {
        var createDatabase = !probe.DatabaseExists;
        var mode = (requested?.Mode ?? "auto").Trim().ToLowerInvariant();
        var access = connection.Windows ? "windows"
            : mode == "same" ? "same"
            : mode == "dedicated" ? "dedicated"
            : probe.CanCreateLogin ? "dedicated" : "same";
        var login = access == "dedicated" ? (requested?.Login?.Trim() is { Length: > 0 } named ? named : SqlServerProvisioner.DefaultLoginName()) : null;
        string? blocker =
            createDatabase && !probe.CanCreateDatabase ? "no-permission-database"
            : !createDatabase && !probe.CanOpenDatabase ? "no-access"
            : access == "dedicated" && !probe.CanCreateLogin ? "no-permission-login"
            : null;
        return new SetupPlan(createDatabase, access, login, blocker);
    }

    private static string BlockerText(string blocker, string database) => blocker switch
    {
        "no-permission-database" => $"ฐานข้อมูล {database} ยังไม่มี และบัญชีนี้สร้างไม่ได้ ต้องเป็น sysadmin หรือมีบทบาท dbcreator (ใช้บัญชี sa หรือให้ผู้ดูแล SQL สร้างฐานข้อมูลนี้ไว้ก่อน)",
        "no-access" => $"ฐานข้อมูล {database} มีอยู่แล้ว แต่บัญชีนี้เปิดไม่ได้ ให้ผู้ดูแล SQL เพิ่มบัญชีนี้เป็นผู้ใช้ของฐานข้อมูลนั้น หรือใช้บัญชี sa",
        "no-permission-login" => "บัญชีนี้สร้างบัญชีสำหรับโปรแกรมไม่ได้ (ต้องเป็น sysadmin หรือ securityadmin) เลือก \"ใช้บัญชีนี้เป็นบัญชีของโปรแกรมด้วย\" หรือใช้บัญชี sa",
        _ => "ทำต่อไม่ได้",
    };

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

/// <summary>Which account the application itself will use: "auto" (its own login when the account may create one), "dedicated", or "same" (the account typed in, kept encrypted in the settings).</summary>
public sealed record SetupAppAccess(string? Mode, string? Login);

public sealed record SetupTestRequest(SetupConnection? Connection, SetupAppAccess? AppAccess);

public sealed record SetupApplyRequest(SetupConnection? Connection, RepairRequest? Repair, SetupAdmin? Admin, SetupAppAccess? AppAccess = null);

/// <summary>What "start" will do: create the database (or find it), which account the application uses, and what, if anything, stops it.</summary>
public sealed record SetupPlan(bool CreateDatabase, string Access, string? Login, string? Blocker);
