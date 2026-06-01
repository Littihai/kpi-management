namespace KPI.Application.Auth;

public record AuthResponse(
    string Token,
    string Email,
    string FullName,
    string Role
);