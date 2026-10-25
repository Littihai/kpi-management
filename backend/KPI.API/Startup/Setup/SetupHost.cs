using System.Net;
using KPI.API.Controllers;
using KPI.Application.Database;
using KPI.Application.Settings;
using KPI.API.Security;
using Microsoft.Data.SqlClient;
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

    /// <summary>One apply at a time: two overlapping ones could each reset the login's password and leave the saved file with the other's.</summary>
    public SemaphoreSlim ApplyLock { get; } = new(1, 1);

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
        if (!DatabaseProvider.IsConfigured(config))
        {
            if (DatabaseProvider.StoredPasswordUnreadable(config))
            {
                log.LogWarning("SETUP: the saved database password cannot be read with this server's key (the key was changed, or the settings file came from another server); connect the database again");
                return new SetupNeed(SetupReason.NotConfigured, "password-unreadable");
            }
            return new SetupNeed(SetupReason.NotConfigured);
        }

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
        var explicitKey = !string.IsNullOrWhiteSpace(builder.Configuration["Setup:Key"]);
        if (explicitKey && builder.Configuration["Setup:Key"]!.Length < 8)
            log.LogError("SETUP: Setup:Key is shorter than 8 characters and is ignored; a key was generated instead (see setup-key.txt beside the settings file)");
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
        else if (key.FromConfiguration) log.LogWarning("SETUP: the database is not ready ({Reason}). Open the site and enter the setup key (Setup:Key in the configuration)", need.Reason);
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
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();   // no idle session of the account that was typed in (sa) outlives the setup
        if (completed) key?.Discard();
        return completed;
    }

    /// <summary>While the database is configured but not answering, keep trying: when it comes back, carry on without anyone doing anything.</summary>
    public static async Task WatchAsync(IConfiguration config, SetupState state, ILogger log, CancellationToken stopping,
        Func<IConfiguration, CancellationToken, Task<(bool Ok, string? Problem, string? Message)>>? connect = null, TimeSpan? interval = null)
    {
        connect ??= SetupMode.TryConnectAsync;
        while (!stopping.IsCancellationRequested && !state.Completed.IsCompleted)
        {
            await Task.Delay(interval ?? TimeSpan.FromSeconds(10), stopping);
            var (ok, problem, _) = await connect(config, stopping);
            if (ok) { log.LogInformation("The database answers again; starting"); state.Complete(); return; }
            if (problem == "login-failed")
            {
                // a wrong password does not fix itself, and every try counts against the login (SQL Server locks a login out after a few)
                log.LogWarning("SETUP: the saved login was refused; not trying again until it is set up again on the setup page");
                await Task.Delay(Timeout.Infinite, stopping);
            }
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

            // The Windows app's own page (app://local) may call the setup API of the server on the same PC: it is as much "its own page"
            // as the server's web page is. Only the exact origin, and only from where the guard would let the call through anyway.
            if (path.StartsWithSegments("/api/setup") && IsDesktopOrigin(context) && (IsLocal(context) || state.AllowRemote))
            {
                context.Response.Headers.AccessControlAllowOrigin = DesktopOrigin;
                context.Response.Headers.Vary = "Origin";
                if (HttpMethods.IsOptions(context.Request.Method))
                {
                    context.Response.Headers.AccessControlAllowMethods = "GET, POST";
                    context.Response.Headers.AccessControlAllowHeaders = "content-type, x-setup-key";
                    context.Response.Headers.AccessControlMaxAge = "600";
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Task.CompletedTask;
                }
            }
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

        // Anyone who can reach the port may ask whether setup is needed. Where things are kept is told only to the browser on the server itself
        // asking for its own page: a request from another origin (a web page, the desktop app, another machine) gets the plain answer, which is
        // also the one that may be read cross-origin.
        app.MapGet("/api/setup/status", (HttpContext http) =>
        {
            var trusted = IsLocal(http) && (!http.Request.Headers.ContainsKey("Origin") || IsDesktopOrigin(http));
            return Results.Json(new
            {
                mode = "setup",
                reason = state.Need.Reason.ToString(),
                problem = state.Need.Problem,
                engine = "SqlServer",
                remoteAllowed = state.AllowRemote,
                canWriteConfig = state.CanWriteConfig,
                jwtConfigured = JwtKeyValidator.Problem(state.Configuration["Jwt:Key"]) is null,
                keyRequired = state.Key is not null && (!IsLocal(http) || state.KeyForLocal),
                message = trusted && state.Need.Reason == SetupReason.StartupFailed ? state.Need.Message : null,
                keyFile = trusted ? state.Key?.File : null,
                machineLogin = trusted ? SqlServerProvisioner.DefaultLoginName() : null,
                current = trusted && !string.IsNullOrWhiteSpace(state.Configuration["Database:Server"]) && !string.IsNullOrWhiteSpace(state.Configuration["Database:Name"])
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

            var probe = await SqlServerProvisioner.ProbeAsync(connection.Access(), connection.Database!.Trim(), cancellationToken, LoginFor(request.AppAccess));
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
                jwtConfigured = JwtKeyValidator.Problem(state.Configuration["Jwt:Key"]) is null,
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
            if (!connection.Windows && JwtKeyValidator.Problem(state.Configuration["Jwt:Key"]) is { } keyProblem)
                return json(new { error = "jwt-missing", message = $"Jwt__Key ของเซิร์ฟเวอร์ใช้ไม่ได้ ({keyProblem}) ตั้งค่าให้ถูกต้องก่อน เพราะใช้เข้ารหัสรหัสผ่านฐานข้อมูลที่บันทึก" }, 400);

            // the administrator is checked before the server is touched: a form that will be refused must change nothing
            if (request.Admin is { } asked && !string.IsNullOrWhiteSpace(asked.Email) && InvalidAdmin(asked) is { } wrong)
                return json(new { error = "admin", message = wrong }, 400);

            if (!await state.ApplyLock.WaitAsync(0, cancellationToken))
                return json(new { error = "busy", message = "กำลังตั้งค่าอยู่ รอให้เสร็จก่อน" }, 409);
            try
            {
            var log = loggers.CreateLogger("Setup");
            var database = connection.Database!.Trim();
            var access = connection.Access();
            var probe = await SqlServerProvisioner.ProbeAsync(access, database, cancellationToken, LoginFor(request.AppAccess));
            if (!probe.Connected) return json(new { error = "not-connected", server = probe, message = "เชื่อมต่อ SQL Server ไม่ได้ (" + (probe.Problem ?? "other") + ")" }, 400);
            var plan = Plan(connection, request.AppAccess, probe);
            if (plan.Blocker is { } blocker) return json(new { error = blocker, message = BlockerText(blocker, database), server = probe, plan }, 400);
            // A login of that name exists: it will get a new password, which stops whatever uses it now. Only with the person's say-so.
            if (plan is { Access: "dedicated", LoginExists: true } && request.AppAccess?.ConfirmReset != true)
                return json(new { error = "login-exists", message = $"มีบัญชี {plan.Login} อยู่บน SQL Server แล้ว ถ้าเป็นของเครื่องนี้เองที่ติดตั้งใหม่ ให้ยืนยันเพื่อตั้งรหัสผ่านใหม่ แต่ถ้าเป็นของเครื่องอื่น เครื่องนั้นจะต่อไม่ได้ ให้ตั้งชื่ออื่นใน \"ตัวเลือกเพิ่มเติม\"", server = probe, plan }, 409);

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
                // anything but done (missing field, bad e-mail, refused password, e-mail already used by someone who is not an administrator):
                // nothing is saved, or the install would end with nobody who can sign in
                if (code != 0) return json(new { error = "admin", message = AdminMessage(code, message), report, done }, 400);
                report = await DatabaseDoctor.InspectAsync(db, cancellationToken);
            }

            string? protectedPassword = application.Windows ? null : DatabaseConfigFile.Protect(state.Configuration, appPassword!);
            DatabaseConfigFile.Write(state.ConfigFile, application, protectedPassword);
            log.LogWarning("SETUP: the database {Database} on {Server} was saved to {File}; starting the application", database, connection.Server, state.ConfigFile);

            // answer first, then let the supervisor swap this page for the application
            _ = Task.Delay(1500, CancellationToken.None).ContinueWith(_ => state.Complete(), TaskScheduler.Default);
            SqlConnection.ClearAllPools();   // the sessions of the account that was typed in (sa) end here
            return json(new { saved = true, createdDatabase = done.Contains("create-database"), access = plan.Access, login = plan.Login, done, admin, report }, 200);
            }
            catch (SqlException ex)
            {
                loggers.CreateLogger("Setup").LogError(ex, "SETUP: SQL Server refused a step");
                return json(new { error = "sql-error", message = ex.Message }, 400);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                loggers.CreateLogger("Setup").LogError(ex, "SETUP: apply failed");
                return json(new { error = "failed", message = ex.GetBaseException().Message }, 500);
            }
            finally { state.ApplyLock.Release(); }
        });

        // anything else under /api: the application is not running yet
        app.Map("/api/{**rest}", () => Results.Json(new { setupRequired = true }, statusCode: 503));
    }

    private static string AdminMessage(int code, string message) => code switch
    {
        4 => "อีเมลนี้เป็นของผู้ใช้อื่นที่ไม่ใช่ผู้ดูแลระบบอยู่แล้วในฐานข้อมูลนี้ ใช้อีเมลอื่น",
        3 or 2 => message,
        _ => message.Length > 0 ? message : "สร้างผู้ดูแลระบบไม่สำเร็จ",
    };

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

    private static readonly string[] ForwardingHeaders = ["X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Forwarded", "X-Real-IP"];

    /// <summary>
    /// A request that really comes from a browser on the server itself: from the loopback address, addressed to a local name (a web page on
    /// another site cannot make the browser use "localhost" after DNS rebinding), and not passed on by a reverse proxy (to which every request
    /// would look like it came from this machine).
    /// </summary>
    private static bool IsLocal(HttpContext http)
    {
        if (!IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None)) return false;
        var host = http.Request.Host.Host;
        var localName = host is "localhost" or "127.0.0.1" or "[::1]" or "::1" || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        return localName && !ForwardingHeaders.Any(http.Request.Headers.ContainsKey);
    }

    /// <summary>Where the Windows app's own pages come from (see desktop/main.cjs). A web page cannot send this Origin: the browser sets it.</summary>
    private const string DesktopOrigin = "app://local";

    private static bool IsDesktopOrigin(HttpContext http) =>
        http.Request.Headers.TryGetValue("Origin", out var origin) && string.Equals(origin.ToString(), DesktopOrigin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A browser sends Origin on a cross-site request. This API is only ever called by its own page (the server's web page, or the Windows
    /// app's), so any other origin is refused.
    /// </summary>
    private static bool IsCrossOrigin(HttpContext http) =>
        http.Request.Headers.TryGetValue("Origin", out var origin) && origin.Count > 0 && !IsDesktopOrigin(http)
        && !(Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri) && string.Equals(uri.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase));

    private static IResult? Guard(HttpContext http, SetupState state)
    {
        var local = IsLocal(http);
        if (!local && !state.AllowRemote)
            return Results.Json(new { error = "local-only", message = "หน้านี้เปิดได้จากตัวเซิร์ฟเวอร์เท่านั้น (เปิดเว็บบนเครื่องเซิร์ฟเวอร์ด้วย http://localhost:พอร์ต โดยตรง ไม่ผ่าน proxy)" }, statusCode: 403);
        if (IsCrossOrigin(http))
            return Results.Json(new { error = "cross-origin", message = "คำขอมาจากหน้าเว็บของที่อื่น จึงไม่รับ" }, statusCode: 403);
        // On the server itself there is nothing to prove unless a key was set on purpose; from anywhere else the key is needed.
        if (state.Key is not { } key || (local && !state.KeyForLocal)) return null;
        switch (key.Authorize(http.Request.Headers["X-Setup-Key"].ToString(), out var wait))
        {
            case SetupKey.Outcome.Blocked:
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Results.Json(new { error = "too-many-attempts", message = "ใส่รหัสผิดหลายครั้ง รอสักครู่แล้วลองใหม่", retryAfterSeconds = (int)wait.TotalSeconds }, statusCode: 429);
            case SetupKey.Outcome.Wrong:
                return Results.Json(new { error = "bad-key", message = "รหัสตั้งค่าไม่ถูกต้อง" }, statusCode: 401);
            default:
                return null;
        }
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
            login is not null && !SqlServerProvisioner.IsValidLoginName(login) ? "invalid-login"
            : createDatabase && !probe.CanCreateDatabase ? "no-permission-database"
            : !createDatabase && !probe.CanOpenDatabase ? "no-access"
            : access == "dedicated" && !probe.CanCreateLogin ? "no-permission-login"
            : null;
        return new SetupPlan(createDatabase, access, login, blocker, access == "dedicated" && probe.LoginExists);
    }

    /// <summary>The login name this request would use for the application (so the probe can say whether it is already there).</summary>
    private static string LoginFor(SetupAppAccess? requested) =>
        requested?.Login?.Trim() is { Length: > 0 } named ? named : SqlServerProvisioner.DefaultLoginName();

    private static string BlockerText(string blocker, string database) => blocker switch
    {
        "no-permission-database" => $"ฐานข้อมูล {database} ยังไม่มี และบัญชีนี้สร้างไม่ได้ ต้องเป็น sysadmin หรือมีบทบาท dbcreator (ใช้บัญชี sa หรือให้ผู้ดูแล SQL สร้างฐานข้อมูลนี้ไว้ก่อน)",
        "no-access" => $"ฐานข้อมูล {database} มีอยู่แล้ว แต่บัญชีนี้เปิดไม่ได้ ให้ผู้ดูแล SQL เพิ่มบัญชีนี้เป็นผู้ใช้ของฐานข้อมูลนั้น หรือใช้บัญชี sa",
        "invalid-login" => $"ชื่อบัญชีของเครื่องนี้ต้องขึ้นต้นด้วย {SqlServerProvisioner.LoginPrefix} และใช้ได้เฉพาะตัวอักษรอังกฤษ ตัวเลข และ _ (ไม่เกิน 100 ตัว)",
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
public sealed record SetupAppAccess(string? Mode, string? Login, bool ConfirmReset = false);

public sealed record SetupTestRequest(SetupConnection? Connection, SetupAppAccess? AppAccess);

public sealed record SetupApplyRequest(SetupConnection? Connection, RepairRequest? Repair, SetupAdmin? Admin, SetupAppAccess? AppAccess = null);

/// <summary>What "start" will do: create the database (or find it), which account the application uses, and what, if anything, stops it.</summary>
public sealed record SetupPlan(bool CreateDatabase, string Access, string? Login, string? Blocker, bool LoginExists = false);
