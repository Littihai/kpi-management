using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Channels;
using KPI.API.Security;
using KPI.Domain.Entities;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KPI.API.Audit;

/// <summary>A bounded in-memory queue between requests and the database writer; the oldest entry is dropped if it fills.</summary>
public sealed class RequestLogQueue
{
    private readonly Channel<RequestLog> _channel = Channel.CreateBounded<RequestLog>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(RequestLog entry) => _channel.Writer.TryWrite(entry);
    public ChannelReader<RequestLog> Reader => _channel.Reader;
}

/// <summary>
/// Records every write, refusal, error and sign-in (not successful reads) with who/where, and times every request.
/// Entries go to a queue and are saved in the background, so a request never waits for the audit write.
/// </summary>
public sealed class RequestLogMiddleware
{
    private const int SlowRequestMs = 800;

    private readonly RequestDelegate _next;
    private readonly RequestLogQueue _queue;
    private readonly AnonymousRequestLogGate _anonymous;
    private readonly ILogger<RequestLogMiddleware> _logger;
    private readonly string? _ipHeader;

    public RequestLogMiddleware(
        RequestDelegate next,
        RequestLogQueue queue,
        AnonymousRequestLogGate anonymous,
        IOptions<LoginThrottleOptions> options,
        ILogger<RequestLogMiddleware> logger)
    {
        _next = next;
        _queue = queue;
        _anonymous = anonymous;
        _logger = logger;
        _ipHeader = options.Value.ClientIpHeader;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();

        // Visible in the browser's network tab (Timing → Server Timing) to tell server time from network time.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Server-Timing"] = $"app;dur={timer.ElapsedMilliseconds}";
            return Task.CompletedTask;
        });

        try
        {
            await _next(context);
        }
        finally
        {
            timer.Stop();
            Record(context, (int)timer.ElapsedMilliseconds);
        }
    }

    private void Record(HttpContext context, int durationMs)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase)
            || context.Request.Method == HttpMethods.Options)
            return;

        var status = context.Response.StatusCode;
        if (durationMs >= SlowRequestMs)
            _logger.LogWarning("Slow request: {Method} {Path} took {Duration} ms ({Status})",
                context.Request.Method, path, durationMs, status);

        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        var isExport = path.StartsWith("/api/export", StringComparison.OrdinalIgnoreCase);
        if (isRead && !isExport && status < 400) return;

        var user = context.User;
        var ip = LoginThrottle.ClientIp(context, _ipHeader);

        // Someone who is not signed in: paths that do not exist are only scanner noise, and the rest is rationed so a
        // flood cannot push real entries out of the queue or fill the table.
        if (user.Identity?.IsAuthenticated != true
            && (status is StatusCodes.Status404NotFound or StatusCodes.Status405MethodNotAllowed || !_anonymous.Allow(ip)))
            return;

        var attemptedEmail = context.Items["audit.email"] as string;

        _queue.Enqueue(new RequestLog
        {
            CreatedAt = DateTime.UtcNow,
            UserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null,
            UserEmail = Truncate(user.FindFirstValue(ClaimTypes.Email) ?? attemptedEmail, 254),
            UserRole = Truncate(user.FindFirstValue(ClaimTypes.Role), 50),
            Method = Truncate(context.Request.Method, 10)!,
            Path = Truncate(path, 300)!,
            StatusCode = status,
            DurationMs = durationMs,
            IpAddress = Truncate(ip, 64),
            UserAgent = Truncate(context.Request.Headers.UserAgent.ToString(), 400),
            RequestId = Truncate(context.TraceIdentifier, 64),
        });
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Saves queued request logs in small batches.</summary>
public sealed class RequestLogWriter : BackgroundService
{
    private readonly RequestLogQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RequestLogWriter> _logger;
    private readonly TimeSpan _interval;

    public RequestLogWriter(
        RequestLogQueue queue,
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<RequestLogWriter> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
        _interval = TimeSpan.FromMilliseconds(int.TryParse(config["Audit:FlushMs"], out var ms) ? Math.Max(50, ms) : 2000);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<RequestLog>(100);
        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // Let a burst of requests collect so they are saved with one round trip.
                await Task.Delay(_interval, stoppingToken);
                while (batch.Count < 200 && _queue.Reader.TryRead(out var entry)) batch.Add(entry);
                await SaveAsync(batch);
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down: write what is left
        }

        while (_queue.Reader.TryRead(out var entry)) batch.Add(entry);
        if (batch.Count > 0) await SaveAsync(batch);
    }

    private async Task SaveAsync(List<RequestLog> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RequestLogs.AddRange(batch);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit logging must never take the API down.
            _logger.LogWarning(ex, "Could not save {Count} request log entries", batch.Count);
        }
    }
}
