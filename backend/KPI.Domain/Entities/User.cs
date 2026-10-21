namespace KPI.Domain.Entities;

public class User : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>When the password was last set. Null for accounts that never changed the one they were given.</summary>
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>The person must choose a new password before doing anything else (set after an administrator sets one, if the policy says so).</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Wrong passwords since the last good sign-in or the last lock.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Sign-in is refused until this moment (too many wrong passwords).</summary>
    public DateTime? LockedUntil { get; set; }

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>"First Last" for display and audit rows. Not stored.</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}