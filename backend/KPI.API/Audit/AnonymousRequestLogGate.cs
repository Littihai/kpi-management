namespace KPI.API.Audit;

/// <summary>
/// Keeps requests from people who are not signed in from flooding the request log. Every refusal is worth one row, but
/// a script hammering the API would otherwise write thousands and push real entries out of the queue and the table.
/// Per address and overall, a fixed number of rows is allowed each minute; the rest are dropped. Signed-in requests
/// never pass through here, and failed sign-ins are also kept in the sign-in log, which has its own throttle.
/// Counters live in memory and reset on restart.
/// </summary>
public sealed class AnonymousRequestLogGate
{
    private const int MaxTrackedAddresses = 10_000;

    private readonly record struct Counter(long Minute, int Count);

    private readonly Dictionary<string, Counter> _perAddress = new();
    private readonly object _gate = new();
    private readonly int _perAddressLimit;
    private readonly int _overallLimit;
    private readonly TimeProvider _time;
    private Counter _overall;

    /// <param name="perAddressPerMinute">Rows one address may write per minute; 0 or less means no rows at all from anonymous callers.</param>
    /// <param name="overallPerMinute">Rows all anonymous callers together may write per minute.</param>
    public AnonymousRequestLogGate(int perAddressPerMinute, int overallPerMinute, TimeProvider time)
    {
        _perAddressLimit = perAddressPerMinute;
        _overallLimit = overallPerMinute;
        _time = time;
    }

    /// <summary>Reads Audit:AnonymousPerAddressPerMinute (20) and Audit:AnonymousPerMinute (300).</summary>
    public static AnonymousRequestLogGate From(IConfiguration config, TimeProvider time) => new(
        int.TryParse(config["Audit:AnonymousPerAddressPerMinute"], out var perAddress) ? perAddress : 20,
        int.TryParse(config["Audit:AnonymousPerMinute"], out var overall) ? overall : 300,
        time);

    /// <summary>True when this request may be written to the log; counts it.</summary>
    public bool Allow(string address)
    {
        var minute = _time.GetUtcNow().ToUnixTimeSeconds() / 60;

        lock (_gate)
        {
            if (_overall.Minute != minute) _overall = new Counter(minute, 0);
            if (_overall.Count >= _overallLimit) return false;

            _perAddress.TryGetValue(address, out var mine);
            if (mine.Minute != minute) mine = new Counter(minute, 0);
            if (mine.Count >= _perAddressLimit) return false;

            if (!_perAddress.ContainsKey(address) && _perAddress.Count >= MaxTrackedAddresses)
            {
                foreach (var old in _perAddress.Where(p => p.Value.Minute != minute).Select(p => p.Key).ToList())
                    _perAddress.Remove(old);
                if (_perAddress.Count >= MaxTrackedAddresses) return false; // flooded by new addresses: write nothing more
            }

            _perAddress[address] = mine with { Count = mine.Count + 1 };
            _overall = _overall with { Count = _overall.Count + 1 };
            return true;
        }
    }
}
