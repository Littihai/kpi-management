namespace KPI.Domain.Entities;

/// <summary>A previous password (its hash), kept so that recent ones cannot be reused.</summary>
public class PasswordHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One security event about signing in or about a password: who, from where, and whether it worked.</summary>
public class AuthLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Event { get; set; } = string.Empty;
    public bool Success { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>The address that was typed, even when no such account exists.</summary>
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>Why it failed: unknown_user, bad_password, inactive, locked.</summary>
    public string? Reason { get; set; }
    public string? Detail { get; set; }
}

public static class AuthEvents
{
    public const string LoginSuccess = "login_success";
    public const string LoginFailed = "login_failed";
    public const string AccountLocked = "account_locked";
    public const string AccountUnlocked = "account_unlocked";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";

    public static readonly string[] All = [LoginSuccess, LoginFailed, AccountLocked, AccountUnlocked, PasswordChanged, PasswordReset];
}
