using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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

    private readonly AppDbContext _db;
    private readonly PublicIntakeLedger _ledger;
    private readonly PublicSubmitOptions _options;
    private readonly TimeProvider _time;

    public PublicIntakeBudget(AppDbContext db, PublicIntakeLedger ledger, IOptions<PublicSubmitOptions> options, TimeProvider time)
    {
        _db = db;
        _ledger = ledger;
        _options = options.Value;
        _time = time;
    }

    /// <summary>
    /// Takes room for one report carrying <paramref name="incomingBytes"/> of attachments, or throws a 429 when the day is
    /// full. Dispose the result once the report has been saved (or has failed) so the room it held is released.
    /// </summary>
    public async Task<IDisposable> ReserveAsync(long incomingBytes)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var since = now - Window;

        await _ledger.Gate.WaitAsync();
        try
        {
            if (_options.MaxPerDay > 0)
            {
                var recent = _db.Issues.AsNoTracking().Where(i => i.IsPublicSubmission && i.CreatedAt >= since);
                if (await recent.CountAsync() + Volatile.Read(ref _ledger.PendingReports) >= _options.MaxPerDay)
                    throw Full(now, await recent.MinAsync(i => (DateTime?)i.CreatedAt));
            }

            if (_options.MaxUploadMbPerDay > 0 && incomingBytes > 0)
            {
                var recent = _db.IssueAttachments.AsNoTracking().Where(a => a.Issue.IsPublicSubmission && a.CreatedAt >= since);
                var used = (await recent.SumAsync(a => (long?)a.SizeBytes) ?? 0) + Volatile.Read(ref _ledger.PendingBytes);
                if (used + incomingBytes > _options.MaxUploadMbPerDay * 1024L * 1024L)
                    throw Full(now, await recent.MinAsync(a => (DateTime?)a.CreatedAt));
            }

            Interlocked.Increment(ref _ledger.PendingReports);
            Interlocked.Add(ref _ledger.PendingBytes, incomingBytes);
        }
        finally
        {
            _ledger.Gate.Release();
        }

        return new Reservation(_ledger, incomingBytes);
    }

    private static TooManyRequestsException Full(DateTime now, DateTime? oldest)
    {
        // Room frees up when the oldest report in the window ages out; never tell a caller to retry in under a minute.
        var wait = oldest is { } o ? o + Window - now : TimeSpan.FromHours(1);
        if (wait < TimeSpan.FromMinutes(1)) wait = TimeSpan.FromMinutes(1);
        return new TooManyRequestsException(
            "แบบฟอร์มนี้รับเรื่องเต็มชั่วคราว กรุณาลองใหม่ภายหลัง หรือติดต่อฝ่าย IT โดยตรง", wait);
    }

    private sealed class Reservation(PublicIntakeLedger ledger, long bytes) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1) return;
            Interlocked.Decrement(ref ledger.PendingReports);
            Interlocked.Add(ref ledger.PendingBytes, -bytes);
        }
    }
}
