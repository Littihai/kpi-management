using KPI.Application.Common;
using KPI.Domain.Entities;

namespace KPI.Application.Auth;

/// <summary>The rules a new password has to meet, so a screen can show them before the person types.</summary>
public record PasswordPolicyInfo(
    int MinLength,
    bool RequireUpper,
    bool RequireLower,
    bool RequireDigit,
    bool RequireSpecial,
    int HistoryCount,
    int MaxAgeDays);

public interface IPasswordPolicy
{
    Task<PasswordPolicyInfo> DescribeAsync();

    /// <summary>Throws RequestValidationException (in Thai) naming every rule the password breaks.</summary>
    Task EnsureAsync(string? password);
}

public enum PasswordSetKind
{
    /// <summary>The person changed their own password.</summary>
    Self,
    /// <summary>An administrator set it (reset, or the first password of a new account).</summary>
    AdminSet,
}

public interface IPasswordService
{
    /// <summary>
    /// Checks the policy and the history, then sets the password: hash, date, history, and clears any lock.
    /// Saves. After an administrator sets one, the person may be made to choose their own (settings).
    /// </summary>
    Task SetAsync(User user, string? newPassword, PasswordSetKind kind);

    /// <summary>The same for a brand-new account that is about to be saved by the caller: nothing is saved here.</summary>
    Task PrepareNewAsync(User user, string? password);
}

public record AuthLogDto(
    Guid Id, DateTime At, string Event, bool Success, Guid? UserId, string? Email,
    string? IpAddress, string? UserAgent, string? Reason, string? Detail);

public record AuthLogQuery(
    string? Search = null, string? Event = null, bool? Success = null,
    DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 25);

public interface IAuthLog
{
    /// <summary>Records an event. Never throws: a failure to log must not stop someone signing in.</summary>
    Task WriteAsync(string eventName, bool success, Guid? userId, string? email, string? reason = null, string? detail = null);

    Task<PagedResult<AuthLogDto>> QueryAsync(AuthLogQuery query);
    Task<int> PurgeOlderThanAsync(DateTime cutoff);
}

public sealed class NullAuthLog : IAuthLog
{
    public static readonly NullAuthLog Instance = new();
    public Task WriteAsync(string eventName, bool success, Guid? userId, string? email, string? reason = null, string? detail = null) => Task.CompletedTask;
    public Task<PagedResult<AuthLogDto>> QueryAsync(AuthLogQuery query) => Task.FromResult(new PagedResult<AuthLogDto>([], 0, 1, query.PageSize));
    public Task<int> PurgeOlderThanAsync(DateTime cutoff) => Task.FromResult(0);
}
