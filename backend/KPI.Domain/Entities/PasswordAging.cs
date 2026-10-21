namespace KPI.Domain.Entities;

public static class PasswordAging
{
    public const string Forced = "forced";
    public const string Expired = "expired";

    /// <summary>Why the person has to choose a new password right now, or null when they do not.</summary>
    public static string? ChangeReason(User user, double maxAgeDays, DateTime now)
    {
        if (user.MustChangePassword) return Forced;
        if (maxAgeDays > 0 && now - (user.PasswordChangedAt ?? user.CreatedAt) > TimeSpan.FromDays(maxAgeDays)) return Expired;
        return null;
    }
}
