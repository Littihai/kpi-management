namespace KPI.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<UserProfileDto> GetProfileAsync(Guid userId);

    /// <summary>Lets a signed-in person change their own password; the current one must be right.</summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}
