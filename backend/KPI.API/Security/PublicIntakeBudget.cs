using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KPI.API.Security;

/// <summary>
/// Reports that have been let in but are not yet saved, shared by every request of this process. Without it, many
/// requests arriving together would all read "plenty of room" from the database before any of them had stored anything.
/// </summary>
public sealed class PublicIntakeLedger
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal int PendingReports;
    internal long PendingBytes;

    // What the database said a moment ago, so a busy form does not add up the attachment table on every submit.
    internal DateTime SnapshotAt = DateTime.MinValue;
    internal int SnapshotReports;
    internal long SnapshotBytes;

    // Per address, over the last 24 hours: when a report was let in and how many attachment bytes it carried.
    internal Dictionary<string, Queue<(DateTime At, long Bytes)>> PerAddress { get; } = new();

    internal DateTime LastWarned = DateTime.MinValue;
}

/// <summary>
/// A ceiling for the whole public (no sign-in) report form, over a rolling 24 hours. The per-address throttle stops one
/// machine; this stops many machines together from filling the database with attachments or burying IT in e-mail.
/// It counts what is stored plus what is being saved right now, so a burst cannot overshoot within one process. With
/// several instances behind one database each only sees the other's saved reports, so a burst across instances can
/// still exceed it a little; treat it as a ceiling, not an exact quota.
/// </summary>
public sealed class PublicIntakeBudget
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan SnapshotLife = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(10);
    private const int MaxTrackedAddresses = 10_000;

    private readonly AppDbContext _db;
    private readonly PublicIntakeLedger _ledger;
    private readonly PublicSubmitOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PublicIntakeBudget>? _log;

    public PublicIntakeBudget(AppDbContext db, PublicIntakeLedger ledger, IOptions<PublicSubmitOptions> options, TimeProvider time,
        ILogger<PublicIntakeBudget>? log = null)
    {
        _log = log;
        _db = db;
        _ledger = ledger;
        _options = options.Value;
        _time = time;
    }

    /// <summary>
    /// Takes room for one report carrying <paramref name="incomingBytes"/> of attachments, or throws a 429 when the day is
    /// full (for everyone, or for <paramref name="address"/> alone). Call <see cref="PublicIntakeReservation.MarkSaved"/> once the
    /// report is stored and dispose the result either way so the room it held is released.
    /// </summary>
    public async Task<PublicIntakeReservation> ReserveAsync(long incomingBytes, string? address = null)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var since = now - Window;
        var byteCap = _options.MaxUploadMbPerDay * 1024L * 1024L;

        // A single report that can never fit would otherwise be told to retry for ever.
        if (_options.MaxUploadMbPerDay > 0 && incomingBytes > byteCap)
            throw new RequestValidationException($"ไฟล์แนบรวมกันใหญ่เกิน {_options.MaxUploadMbPerDay} MB กรุณาลดขนาดหรือจำนวนไฟล์");

        await _ledger.Gate.WaitAsync();
        try
        {
            CheckAddress(address, incomingBytes, now);

            if (_options.MaxPerDay > 0 || (_options.MaxUploadMbPerDay > 0 && incomingBytes > 0))
                await RefreshSnapshotAsync(now, since);

            if (_options.MaxPerDay > 0
                && _ledger.SnapshotReports + Volatile.Read(ref _ledger.PendingReports) >= _options.MaxPerDay)
            {
                var oldest = await _db.Issues.AsNoTracking().Where(i => i.IsPublicSubmission && i.CreatedAt >= since)
                    .MinAsync(i => (DateTime?)i.CreatedAt);
                throw Full(now, oldest, "the day's report ceiling");
            }

            if (_options.MaxUploadMbPerDay > 0 && incomingBytes > 0
                && _ledger.SnapshotBytes + Volatile.Read(ref _ledger.PendingBytes) + incomingBytes > byteCap)
            {
                // Rare path: work out when enough old attachments will have aged out to make room for this one.
                var need = _ledger.SnapshotBytes + Volatile.Read(ref _ledger.PendingBytes) + incomingBytes - byteCap;
                var stored = await _db.IssueAttachments.AsNoTracking()
                    .Where(a => a.Issue.IsPublicSubmission && a.CreatedAt >= since)
                    .OrderBy(a => a.CreatedAt).Select(a => new { a.CreatedAt, a.SizeBytes }).ToListAsync();
                DateTime? frees = null;
                long freed = 0;
                foreach (var a in stored)
                {
                    freed += a.SizeBytes;
                    if (freed >= need) { frees = a.CreatedAt; break; }
                }
                throw Full(now, frees, "the day's attachment ceiling");
            }

            Interlocked.Increment(ref _ledger.PendingReports);
            Interlocked.Add(ref _ledger.PendingBytes, incomingBytes);
            RecordAddress(address, incomingBytes, now);
        }
        finally
        {
            _ledger.Gate.Release();
        }

        return new PublicIntakeReservation(_ledger, incomingBytes);
    }

    private async Task RefreshSnapshotAsync(DateTime now, DateTime since)
    {
        if (now - _ledger.SnapshotAt < SnapshotLife && now >= _ledger.SnapshotAt) return;

        _ledger.SnapshotReports = await _db.Issues.AsNoTracking().CountAsync(i => i.IsPublicSubmission && i.CreatedAt >= since);
        _ledger.SnapshotBytes = await _db.IssueAttachments.AsNoTracking()
            .Where(a => a.Issue.IsPublicSubmission && a.CreatedAt >= since).SumAsync(a => (long?)a.SizeBytes) ?? 0;
        _ledger.SnapshotAt = now;
    }

    /// <summary>One address may not use up the whole day for everybody else.</summary>
    private void CheckAddress(string? address, long incomingBytes, DateTime now)
    {
        if (address is null || (_options.MaxPerAddressPerDay <= 0 && _options.MaxUploadMbPerAddressPerDay <= 0)) return;
        if (!_ledger.PerAddress.TryGetValue(address, out var hits)) return;

        while (hits.Count > 0 && now - hits.Peek().At >= Window) hits.Dequeue();
        if (hits.Count == 0) { _ledger.PerAddress.Remove(address); return; }

        if (_options.MaxPerAddressPerDay > 0 && hits.Count >= _options.MaxPerAddressPerDay)
            throw AddressFull(now, hits.Peek().At);

        var byteCap = _options.MaxUploadMbPerAddressPerDay * 1024L * 1024L;
        if (_options.MaxUploadMbPerAddressPerDay > 0 && incomingBytes > 0)
        {
            var used = hits.Sum(h => h.Bytes);
            if (used + incomingBytes > byteCap)
            {
                var need = used + incomingBytes - byteCap;
                long freed = 0;
                var at = hits.Peek().At;
                foreach (var h in hits)
                {
                    freed += h.Bytes;
                    if (freed >= need) { at = h.At; break; }
                }
                throw AddressFull(now, at);
            }
        }
    }

    private void RecordAddress(string? address, long incomingBytes, DateTime now)
    {
        if (address is null || (_options.MaxPerAddressPerDay <= 0 && _options.MaxUploadMbPerAddressPerDay <= 0)) return;

        if (!_ledger.PerAddress.ContainsKey(address) && _ledger.PerAddress.Count >= MaxTrackedAddresses)
        {
            foreach (var old in _ledger.PerAddress.Where(p => p.Value.Count == 0 || now - p.Value.Peek().At >= Window).Select(p => p.Key).ToList())
                _ledger.PerAddress.Remove(old);
            if (_ledger.PerAddress.Count >= MaxTrackedAddresses) return; // flooded by new addresses: the global ceiling still holds
        }

        if (!_ledger.PerAddress.TryGetValue(address, out var hits))
            _ledger.PerAddress[address] = hits = new Queue<(DateTime At, long Bytes)>();
        hits.Enqueue((now, incomingBytes));
    }

    private static TooManyRequestsException AddressFull(DateTime now, DateTime oldest)
    {
        var wait = oldest + Window - now;
        if (wait < TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1);
        return new TooManyRequestsException("เครื่องนี้ส่งเรื่องครบจำนวนต่อวันแล้ว กรุณาลองใหม่ภายหลัง หรือติดต่อฝ่าย IT โดยตรง", wait);
    }

    private TooManyRequestsException Full(DateTime now, DateTime? oldest, string which)
    {
        // So that IT learns the form is shut for everyone, at most once in a while.
        if (now - _ledger.LastWarned >= WarnEvery)
        {
            _ledger.LastWarned = now;
            _log?.LogWarning("The public report form is full for everyone ({Which}); raise PublicIssues:MaxPerDay / MaxUploadMbPerDay or look for abuse.", which);
        }

        // Room frees up when the report or attachment that must age out does; never tell a caller to retry in under a minute.
        var wait = oldest is { } o ? o + Window - now : TimeSpan.FromHours(1);
        if (wait < TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1);
        return new TooManyRequestsException(
            "แบบฟอร์มนี้รับเรื่องเต็มชั่วคราว กรุณาลองใหม่ภายหลัง หรือติดต่อฝ่าย IT โดยตรง", wait);
    }
}

/// <summary>Room held for one report in flight. <see cref="MarkSaved"/> when stored; Dispose to release the hold.</summary>
public sealed class PublicIntakeReservation(PublicIntakeLedger ledger, long bytes) : IDisposable
{
    private int _released;
    private int _saved;

    /// <summary>The report is now in the database: count it in the cached totals until they are read afresh.</summary>
    public void MarkSaved() => Interlocked.Exchange(ref _saved, 1);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return;
        if (Volatile.Read(ref _saved) == 1)
        {
            Interlocked.Increment(ref ledger.SnapshotReports);
            Interlocked.Add(ref ledger.SnapshotBytes, bytes);
        }
        Interlocked.Decrement(ref ledger.PendingReports);
        Interlocked.Add(ref ledger.PendingBytes, -bytes);
    }
}
