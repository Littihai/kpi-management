namespace KPI.Application.Auth;

public record AuthResponse(
    string Token,
    string Email,
    string FullName,
    string Role,
    Guid? DepartmentId,
    IReadOnlyList<string> Permissions
);

/// <summary>Current user profile returned by <c>GET /api/auth/me</c>.</summary>
public record UserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Permissions
);
