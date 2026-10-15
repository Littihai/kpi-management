using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KPI.Application.Auth;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace KPI.Infrastructure.Services;

public class AuthService : IAuthService
{
    public const int MinPasswordLength = 8;

    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _config;

    public AuthService(IUserRepository userRepository, IConfiguration config)
    {
        _userRepository = userRepository;
        _config = config;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email.Length == 0 || !email.Contains('@'))
            throw new RequestValidationException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new RequestValidationException("First name and last name are required.");
        EnsurePasswordPolicy(request.Password);

        if (await _userRepository.EmailExistsAsync(email))
            throw new RequestValidationException("Email already exists.");

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        };

        await _userRepository.CreateAsync(user);
        var created = await _userRepository.GetByIdWithPermissionsAsync(user.Id);
        return GenerateToken(created!);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByEmailAsync(NormalizeEmail(request.Email));

        // Same message for every failure so the response does not reveal which accounts exist.
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new RequestValidationException("Invalid email or password");

        var withPermissions = await _userRepository.GetByIdWithPermissionsAsync(user.Id);
        return GenerateToken(withPermissions!);
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdWithPermissionsAsync(userId);
        if (user == null || !user.IsActive)
            throw new ForbiddenException("Your account is not active.");

        var department = (await _userRepository.GetByIdAsync(userId))?.Department;
        return new UserProfileDto(
            user.Id,
            user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Role.Name,
            user.DepartmentId,
            department?.Name,
            PermissionNames(user));
    }

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static void EnsurePasswordPolicy(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new RequestValidationException($"Password must be at least {MinPasswordLength} characters.");
    }

    private static IReadOnlyList<string> PermissionNames(User user) =>
        user.Role.RolePermissions.Select(rp => rp.Permission.Name).OrderBy(n => n).ToList();

    private AuthResponse GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var roleName = user.Role?.Name ?? RoleNames.Staff;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, roleName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new AuthResponse(
            Token: new JwtSecurityTokenHandler().WriteToken(token),
            Email: user.Email,
            FullName: $"{user.FirstName} {user.LastName}".Trim(),
            Role: roleName,
            DepartmentId: user.DepartmentId,
            Permissions: user.Role != null ? PermissionNames(user) : []);
    }
}
