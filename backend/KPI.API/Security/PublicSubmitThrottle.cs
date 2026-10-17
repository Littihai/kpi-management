using Microsoft.Extensions.Options;

namespace KPI.API.Security;

public class PublicSubmitOptions
{
    /// <summary>Reports one client address may send without signing in, per window.</summary>
    public int MaxPerWindow { get; set; } = 5;
    public int WindowMinutes { get; set; } = 15;
}

/// <summary>
/// Limits the public (no sign-in) report form so it cannot be used to flood the issue queue. In memory:
/// resets on restart, which is acceptable for a soft limit.
/// </summary>
public sealed class PublicSubmitThrottle
{
    private const int MaxTrackedAddresses = 10_000;

    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new();
    private readonly object _gate = new();
    private readonly PublicSubmitOptions _options;
    private readonly TimeProvider _time;

    public PublicSubmitThrottle(IOptions<PublicSubmitOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    /// <summary>Counts the attempt and returns null, or how long the caller must wait.</summary>
    public TimeSpan? TryAcquire(string ip)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var window = TimeSpan.FromMinutes(Math.Max(1, _options.WindowMinutes));

            if (_hits.Count > MaxTrackedAddresses)
                foreach (var key in _hits.Where(h => h.Value.Count == 0 || now - h.Value.Peek() > window).Select(h => h.Key).ToList())
                    _hits.Remove(key);

            if (!_hits.TryGetValue(ip, out var queue))
                _hits[ip] = queue = new Queue<DateTimeOffset>();

            while (queue.Count > 0 && now - queue.Peek() > window) queue.Dequeue();

            if (queue.Count >= Math.Max(1, _options.MaxPerWindow))
                return window - (now - queue.Peek());

            queue.Enqueue(now);
            return null;
        }
    }
}
