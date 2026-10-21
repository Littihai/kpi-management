using KPI.Application.Auth;
using KPI.Application.Settings;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Settings;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KPI.Infrastructure.Services;

/// <summary>The password rules from the settings screen (length, character kinds).</summary>
public sealed class PasswordPolicyService : IPasswordPolicy
{
    private readonly ISystemSettings _settings;

    public PasswordPolicyService(ISystemSettings settings) => _settings = settings;

    public async Task<PasswordPolicyInfo> DescribeAsync() => new(
        (int)await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordMinLength),
        await _settings.GetBoolAsync(SettingCatalog.Keys.PasswordRequireUpper),
        await _settings.GetBoolAsync(SettingCatalog.Keys.PasswordRequireLower),
        await _settings.GetBoolAsync(SettingCatalog.Keys.PasswordRequireDigit),
        await _settings.GetBoolAsync(SettingCatalog.Keys.PasswordRequireSpecial),
        (int)await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordHistoryCount),
        (int)await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordMaxAgeDays));

    public async Task EnsureAsync(string? password)
    {
        var policy = await DescribeAsync();
        var problems = new List<string>();
        var value = password ?? string.Empty;

        if (value.Length < policy.MinLength) problems.Add($"ยาวอย่างน้อย {policy.MinLength} ตัวอักษร");
        if (value.Length > 128) problems.Add("ยาวไม่เกิน 128 ตัวอักษร");
        if (policy.RequireUpper && !value.Any(c => c is >= 'A' and <= 'Z')) problems.Add("มีตัวพิมพ์ใหญ่ (A-Z)");
        if (policy.RequireLower && !value.Any(c => c is >= 'a' and <= 'z')) problems.Add("มีตัวพิมพ์เล็ก (a-z)");
        if (policy.RequireDigit && !value.Any(c => c is >= '0' and <= '9')) problems.Add("มีตัวเลข (0-9)");
        if (policy.RequireSpecial && !value.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))) problems.Add("มีอักขระพิเศษ เช่น ! @ # $");

        if (problems.Count > 0)
            throw new RequestValidationException("รหัสผ่านต้อง" + string.Join(" และ", problems));
    }
}

/// <summary>Setting a password in one place: policy, history, hash, dates, unlocking, and the event for the log.</summary>
public sealed class PasswordService : IPasswordService
{
    private const int KeepHistory = 24;

    private readonly AppDbContext _db;
    private readonly ISystemSettings _settings;
    private readonly IPasswordPolicy _policy;
    private readonly IAuthLog _log;
    private readonly TimeProvider _time;

    public PasswordService(AppDbContext db, ISystemSettings settings, IPasswordPolicy policy, IAuthLog log, TimeProvider time)
    {
        _db = db;
        _settings = settings;
        _policy = policy;
        _log = log;
        _time = time;
    }

    public async Task SetAsync(User user, string? newPassword, PasswordSetKind kind)
    {
        await _policy.EnsureAsync(newPassword);
        await EnsureNotReusedAsync(user, newPassword!);

        var mustChange = kind == PasswordSetKind.AdminSet && await _settings.GetBoolAsync(SettingCatalog.Keys.ForceChangeOnAdminSet);
        await RememberCurrentIfNewAsync(user);
        Apply(user, newPassword!, mustChange);
        await PruneHistoryAsync(user.Id);
        await _db.SaveChangesAsync();

        await _log.WriteAsync(kind == PasswordSetKind.Self ? AuthEvents.PasswordChanged : AuthEvents.PasswordReset,
            true, user.Id, user.Email);
    }

    public async Task PrepareNewAsync(User user, string? password)
    {
        await _policy.EnsureAsync(password);
        var mustChange = await _settings.GetBoolAsync(SettingCatalog.Keys.ForceChangeOnAdminSet);
        Apply(user, password!, mustChange);
    }

    private void Apply(User user, string password, bool mustChange)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.PasswordChangedAt = now;
        user.MustChangePassword = mustChange;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        _db.PasswordHistory.Add(new PasswordHistory { UserId = user.Id, PasswordHash = user.PasswordHash, CreatedAt = now });
    }

    /// <summary>An account from before the history existed: its current password becomes the first entry, so it counts as "previous" from now on.</summary>
    private async Task RememberCurrentIfNewAsync(User user)
    {
        if (string.IsNullOrEmpty(user.PasswordHash) || await _db.PasswordHistory.AnyAsync(h => h.UserId == user.Id)) return;
        _db.PasswordHistory.Add(new PasswordHistory { UserId = user.Id, PasswordHash = user.PasswordHash, CreatedAt = user.PasswordChangedAt ?? user.CreatedAt });
    }

    private async Task EnsureNotReusedAsync(User user, string password)
    {
        var depth = (int)await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordHistoryCount);
        if (depth <= 0) return;

        var recent = await _db.PasswordHistory.AsNoTracking().Where(h => h.UserId == user.Id)
            .OrderByDescending(h => h.CreatedAt).Take(depth).Select(h => h.PasswordHash).ToListAsync();
        // An account from before the history existed has only its current password to compare with.
        if (!string.IsNullOrEmpty(user.PasswordHash) && !recent.Contains(user.PasswordHash)) recent.Add(user.PasswordHash);

        if (recent.Any(hash => BCrypt.Net.BCrypt.Verify(password, hash)))
            throw new RequestValidationException($"ห้ามใช้รหัสผ่านที่เคยใช้ใน {depth} ครั้งล่าสุดซ้ำ กรุณาตั้งรหัสใหม่");
    }

    private async Task PruneHistoryAsync(Guid userId)
    {
        var old = await _db.PasswordHistory.Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt).Skip(KeepHistory - 1).ToListAsync();   // the new one makes the 24th
        // rows added in this unit of work are not in the database yet, so they are never pruned here
        if (old.Count > 0) _db.PasswordHistory.RemoveRange(old);
    }
}

public sealed class AuthLogService : IAuthLog
{
    private readonly AppDbContext _db;
    private readonly KPI.Application.Audit.IAuditContext _context;
    private readonly TimeProvider _time;
    private readonly Microsoft.Extensions.Logging.ILogger<AuthLogService> _logger;

    public AuthLogService(AppDbContext db, KPI.Application.Audit.IAuditContext context, TimeProvider time,
        Microsoft.Extensions.Logging.ILogger<AuthLogService> logger)
    {
        _db = db;
        _context = context;
        _time = time;
        _logger = logger;
    }

    public async Task WriteAsync(string eventName, bool success, Guid? userId, string? email, string? reason = null, string? detail = null)
    {
        try
        {
            _db.AuthLogs.Add(new AuthLog
            {
                At = _time.GetUtcNow().UtcDateTime,
                Event = eventName,
                Success = success,
                UserId = userId,
                Email = email is null ? null : email.Length > 254 ? email[..254] : email,
                IpAddress = _context.IpAddress is { Length: > 64 } ip ? ip[..64] : _context.IpAddress,
                UserAgent = _context.UserAgent is { Length: > 400 } ua ? ua[..400] : _context.UserAgent,
                Reason = reason,
                Detail = detail is { Length: > 300 } ? detail[..300] : detail,
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the sign-in log entry {Event}", eventName);
        }
    }

    public async Task<KPI.Application.Common.PagedResult<AuthLogDto>> QueryAsync(AuthLogQuery query)
    {
        var rows = _db.AuthLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Event)) rows = rows.Where(r => r.Event == query.Event);
        if (query.Success is { } success) rows = rows.Where(r => r.Success == success);
        if (query.From is { } from) rows = rows.Where(r => r.At >= from);
        if (query.To is { } to) rows = rows.Where(r => r.At < to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(r => (r.Email != null && r.Email.ToLower().Contains(term)) || (r.IpAddress != null && r.IpAddress.Contains(term)));
        }

        var page = KPI.Application.Common.PagedResult<AuthLogDto>.ClampPage(query.Page);
        var size = KPI.Application.Common.PagedResult<AuthLogDto>.ClampSize(query.PageSize, 100);
        var total = await rows.CountAsync();
        var items = await rows.OrderByDescending(r => r.At).Skip((page - 1) * size).Take(size)
            .Select(r => new AuthLogDto(r.Id, r.At, r.Event, r.Success, r.UserId, r.Email, r.IpAddress, r.UserAgent, r.Reason, r.Detail))
            .ToListAsync();
        return new KPI.Application.Common.PagedResult<AuthLogDto>(items, total, page, size);
    }

    public async Task<int> PurgeOlderThanAsync(DateTime cutoff)
    {
        if (_db.Database.IsRelational())
            return await _db.AuthLogs.Where(r => r.At < cutoff).ExecuteDeleteAsync();
        var old = await _db.AuthLogs.Where(r => r.At < cutoff).ToListAsync();
        _db.AuthLogs.RemoveRange(old);
        await _db.SaveChangesAsync();
        return old.Count;
    }
}
