namespace KPI.Application.Auth;

public record AuthResponse(
    string Token,
    string Email,
    string FullName,
    string Role,
    Guid? DepartmentId,
    IReadOnlyList<string> Permissions,
    /// <summary>The person must choose a new password before anything else works ("forced" or "expired").</summary>
    bool PasswordChangeRequired = false,
    string? PasswordChangeReason = null
);

/// <summary>Current user profile returned by <c>GET /api/auth/me</c>.</summary>
public record UserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Permissions,
    bool PasswordChangeRequired = false,
    string? PasswordChangeReason = null
);
