using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KPI.Application.Auth;
using KPI.Domain.Constants;
using KPI.Domain.Entities;
using KPI.Domain.Exceptions;
using KPI.Application.Settings;
using KPI.Domain.Interfaces;
using KPI.Domain.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace KPI.Infrastructure.Services;

public class AuthService : IAuthService
{
    public const int MinPasswordLength = 8;

    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _config;
    private readonly ISystemSettings _settings;
    private readonly IPasswordService _passwords;
    private readonly IAuthLog _log;
    private readonly TimeProvider _time;

    public AuthService(IUserRepository userRepository, IConfiguration config, ISystemSettings settings,
        IPasswordService passwords, IAuthLog log, TimeProvider time)
    {
        _userRepository = userRepository;
        _config = config;
        _settings = settings;
        _passwords = passwords;
        _log = log;
        _time = time;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (email.Length == 0 || !email.Contains('@'))
            throw new RequestValidationException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new RequestValidationException("First name and last name are required.");

        if (await _userRepository.EmailExistsAsync(email))
            throw new RequestValidationException("Email already exists.");

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            RoleId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        };
        await _passwords.PrepareNewAsync(user, request.Password);

        await _userRepository.CreateAsync(user);
        var created = await _userRepository.GetByIdWithPermissionsAsync(user.Id);
        return await GenerateTokenAsync(created!);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _userRepository.GetByEmailAsync(email);
        var now = _time.GetUtcNow().UtcDateTime;

        // Same message for every failure so the response does not reveal which accounts exist.
        const string Invalid = "Invalid email or password";
        if (user == null)
        {
            await _log.WriteAsync(AuthEvents.LoginFailed, false, null, email, "unknown_user");
            throw new RequestValidationException(Invalid);
        }
        if (!user.IsActive)
        {
            await _log.WriteAsync(AuthEvents.LoginFailed, false, user.Id, email, "inactive");
            throw new RequestValidationException(Invalid);
        }
        if (user.LockedUntil is { } until && until > now)
        {
            await _log.WriteAsync(AuthEvents.LoginFailed, false, user.Id, email, "locked");
            var wait = until - now;
            throw new TooManyRequestsException(
                $"บัญชีถูกล็อกชั่วคราวเพราะใส่รหัสผ่านผิดหลายครั้ง ลองใหม่ได้ในอีก {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} นาที หรือขอให้ผู้ดูแลปลดล็อก", wait);
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            var max = (int)await _settings.GetNumberAsync(SettingCatalog.Keys.LoginMaxFailures);
            var locked = false;
            if (max > 0)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= max)
                {
                    var minutes = await _settings.GetNumberAsync(SettingCatalog.Keys.LoginLockMinutes);
                    user.LockedUntil = now.AddMinutes(minutes);
                    user.FailedLoginCount = 0;          // a fresh count when the lock ends
                    locked = true;
                }
                await _userRepository.UpdateAsync(user);
            }
            await _log.WriteAsync(AuthEvents.LoginFailed, false, user.Id, email, "bad_password");
            if (locked) await _log.WriteAsync(AuthEvents.AccountLocked, false, user.Id, email, "too_many_failures");
            throw new RequestValidationException(Invalid);
        }

        if (user.FailedLoginCount > 0 || user.LockedUntil != null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            await _userRepository.UpdateAsync(user);
        }
        await _log.WriteAsync(AuthEvents.LoginSuccess, true, user.Id, email);

        var withPermissions = await _userRepository.GetByIdWithPermissionsAsync(user.Id);
        return await GenerateTokenAsync(withPermissions!);
    }


    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdWithPermissionsAsync(userId);
        if (user == null || !user.IsActive)
            throw new ForbiddenException("Your account is not active.");

        var department = (await _userRepository.GetByIdAsync(userId))?.Department;
        var reason = await ChangeReasonAsync(user);
        return new UserProfileDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Role.Name,
            user.DepartmentId,
            department?.Name,
            PermissionNames(user),
            reason is not null,
            reason);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new ForbiddenException("Your account is not active.");
        if (string.IsNullOrEmpty(request.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw new RequestValidationException("Current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword)
            throw new RequestValidationException("The new password must be different from the current one.");

        await _passwords.SetAsync(user, request.NewPassword, PasswordSetKind.Self);
    }

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Why this person must choose a new password right now ("forced" or "expired"), or null.</summary>
    private async Task<string?> ChangeReasonAsync(User user) =>
        PasswordAging.ChangeReason(user, await _settings.GetNumberAsync(SettingCatalog.Keys.PasswordMaxAgeDays), _time.GetUtcNow().UtcDateTime);

    /// <summary>The floor that applies before any settings exist (the first administrator created at start-up).</summary>
    public static void EnsurePasswordPolicy(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new RequestValidationException($"Password must be at least {MinPasswordLength} characters.");
    }

    private static IReadOnlyList<string> PermissionNames(User user) =>
        user.Role.RolePermissions.Select(rp => rp.Permission.Name).OrderBy(n => n).ToList();

    private async Task<AuthResponse> GenerateTokenAsync(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

        var reason = await ChangeReasonAsync(user);
        var roleName = user.Role?.Name ?? RoleNames.Staff;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName),
            new(ClaimTypes.Name, user.FullName),
        };
        // Lets the audit log record the department without another database lookup.
        if (user.Department?.Name is { Length: > 0 } departmentName)
            claims.Add(new Claim("dept", departmentName));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(await _settings.GetNumberAsync(SettingCatalog.Keys.SessionDays)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new AuthResponse(
            Token: new JwtSecurityTokenHandler().WriteToken(token),
            Email: user.Email,
            FullName: user.FullName,
            Role: roleName,
            DepartmentId: user.DepartmentId,
            Permissions: user.Role != null ? PermissionNames(user) : [],
            PasswordChangeRequired: reason is not null,
            PasswordChangeReason: reason);
    }
}
