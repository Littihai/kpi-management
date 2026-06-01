namespace KPI.Application.Auth;

public record LoginRequest(
    string Email,
    string Password
);