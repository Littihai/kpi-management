using System.Net;
using Microsoft.Extensions.Options;

namespace KPI.API.Security;

/// <summary>Settings for <see cref="LoginThrottle"/>, bound from the "LoginThrottle" configuration section.</summary>
public class LoginThrottleOptions
{
    /// <summary>Failed sign-ins allowed for one e-mail address from one client address per window.</summary>
    public int MaxFailuresPerAccount { get; set; } = 5;

    /// <summary>Failed sign-ins allowed from one client address per window, across all e-mail addresses.</summary>
    public int MaxFailuresPerIp { get; set; } = 20;

    public int WindowMinutes { get; set; } = 15;

    /// <summary>
    /// Header that carries the real client address when the API sits behind a proxy, e.g. "CF-Connecting-IP"
    /// on Render. Empty means "use the socket address". Only set it when every request really passes through
    /// a proxy that overwrites the header; otherwise a caller could forge it and dodge the throttle.
    /// For a list such as X-Forwarded-For the last entry is read, which is right for exactly one proxy that appends
    /// to the list. Behind several proxies, use a single-value header that the outermost one sets.
    /// </summary>
    public string? ClientIpHeader { get; set; }
}

/// <summary>
/// Slows down password guessing. Only failed sign-ins count, so many colleagues behind one office address
/// can all sign in normally. Two limits apply: one per (client address, e-mail) and one per client address,
/// which stops one address from trying a password against many accounts. Counters live in memory and reset
/// on restart or when a request lands on another instance; bcrypt's cost is the backstop.
/// Because the account limit is keyed by client address too, a stranger cannot lock a colleague out.
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxTrackedKeys = 10_000;
    private const int MaxEmailLength = 254;

    private readonly record struct Counter(int Failures, DateTimeOffset WindowEnds);

    private readonly Dictionary<string, Counter> _counters = new();
    private readonly object _gate = new();
    private readonly LoginThrottleOptions _options;
    private readonly TimeProvider _time;

    public LoginThrottle(IOptions<LoginThrottleOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    /// <summary>How long the caller must wait, or null when this attempt may proceed.</summary>
    public TimeSpan? BlockedFor(string ip, string email)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var wait = Remaining(AccountKey(ip, email), _options.MaxFailuresPerAccount, now);
            var ipWait = Remaining(IpKey(ip), _options.MaxFailuresPerIp, now);

            if (wait is null) return ipWait;
            if (ipWait is null) return wait;
            return wait > ipWait ? wait : ipWait;
        }
    }

    public void RecordFailure(string ip, string email)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            Increment(AccountKey(ip, email), now);
            Increment(IpKey(ip), now);
        }
    }

    /// <summary>
    /// Forgives the account counter only. The per-address counter is kept, otherwise someone holding one valid
    /// account could sign in between guesses to keep resetting it.
    /// </summary>
    public void RecordSuccess(string ip, string email)
    {
        lock (_gate)
            _counters.Remove(AccountKey(ip, email));
    }

    private TimeSpan? Remaining(string key, int max, DateTimeOffset now)
    {
        if (max <= 0 || !_counters.TryGetValue(key, out var counter)) return null;
        if (counter.WindowEnds <= now)
        {
            _counters.Remove(key);
            return null;
        }
        return counter.Failures >= max ? counter.WindowEnds - now : null;
    }

    private void Increment(string key, DateTimeOffset now)
    {
        if (_counters.TryGetValue(key, out var counter) && counter.WindowEnds > now)
        {
            _counters[key] = counter with { Failures = counter.Failures + 1 };
            return;
        }

        if (_counters.Count >= MaxTrackedKeys)
        {
            foreach (var expired in _counters.Where(c => c.Value.WindowEnds <= now).Select(c => c.Key).ToList())
                _counters.Remove(expired);

            // Still full of live entries (someone is flooding us with new keys): keep what we track
            // rather than evict a real block, and let the new key through untracked.
            if (_counters.Count >= MaxTrackedKeys) return;
        }

        _counters[key] = new Counter(1, now + TimeSpan.FromMinutes(Math.Max(1, _options.WindowMinutes)));
    }

    private static string AccountKey(string ip, string email) =>
        $"a|{ip}|{(email.Length > MaxEmailLength ? email[..MaxEmailLength] : email)}";

    private static string IpKey(string ip) => $"i|{ip}";

    /// <summary>
    /// The caller's address: the configured proxy header when present and valid, otherwise the socket address.
    /// A header that is missing or not an IP address is ignored, so junk cannot create unbounded keys.
    /// When the header holds a list (X-Forwarded-For), the LAST address is used: the proxy appends the address it
    /// saw to the end, while everything before it was written by the caller and can be forged.
    /// </summary>
    public static string ClientIp(HttpContext context, string? headerName)
    {
        if (!string.IsNullOrWhiteSpace(headerName)
            && context.Request.Headers.TryGetValue(headerName, out var values)
            && values.LastOrDefault()?.Split(',')[^1].Trim() is { Length: > 0 } candidate
            && IPAddress.TryParse(candidate, out var parsed))
        {
            return parsed.ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
