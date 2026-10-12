using System.Security.Claims;
using KPI.Application.Auth;
using KPI.Domain.Constants;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Data;
using KPI.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>SuperAdmin and Director see everyone; a Manager sees only their own department.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.User.View)]
    public async Task<IActionResult> GetAll()
    {
        var caller = await _context.Users.Include(u => u.Role).AsNoTracking()
            .FirstAsync(u => u.Id == CurrentUserId);

        var query = _context.Users.AsQueryable();
        if (!RoleNames.SeesAllDepartments(caller.Role.Name))
        {
            var departmentId = caller.DepartmentId;
            query = query.Where(u => departmentId != null && u.DepartmentId == departmentId);
        }

        var users = await query
            .OrderBy(u => u.FirstName)
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.IsActive,
                Role = u.Role.Name,
                Department = u.Department != null ? u.Department.Name : null
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.User.Create)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var email = AuthService.NormalizeEmail(request.Email);
        if (email.Length == 0 || !email.Contains('@'))
            throw new RequestValidationException("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new RequestValidationException("First name and last name are required.");
        AuthService.EnsurePasswordPolicy(request.Password);

        if (!Guid.TryParse(request.RoleId, out var roleId) || !await _context.Roles.AnyAsync(r => r.Id == roleId))
            throw new RequestValidationException("Role not found.");

        Guid? departmentId = null;
        if (!string.IsNullOrWhiteSpace(request.DepartmentId))
        {
            if (!Guid.TryParse(request.DepartmentId, out var parsed)
                || !await _context.Departments.AnyAsync(d => d.Id == parsed && d.IsActive))
                throw new RequestValidationException("Department not found.");
            departmentId = parsed;
        }

        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email))
            throw new RequestValidationException("Email already exists.");

        _context.Users.Add(new KPI.Domain.Entities.User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = roleId,
            DepartmentId = departmentId,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "User created" });
    }

    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.User.Edit)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        if (id == CurrentUserId)
            throw new InvalidStateException("You cannot deactivate your own account.");

        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("User not found.");

        if (user.IsActive && user.Role.Name == RoleNames.SuperAdmin)
            await EnsureAnotherActiveSuperAdminAsync(user.Id);

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "User deactivated" });
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Policy = Permissions.User.Edit)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeRoleRequest request)
    {
        if (id == CurrentUserId)
            throw new InvalidStateException("You cannot change your own role.");

        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new NotFoundException("User not found.");

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.RoleName)
            ?? throw new RequestValidationException("Role not found.");

        if (user.IsActive && user.Role.Name == RoleNames.SuperAdmin && role.Name != RoleNames.SuperAdmin)
            await EnsureAnotherActiveSuperAdminAsync(user.Id);

        user.RoleId = role.Id;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Role updated" });
    }

    /// <summary>The system must always keep at least one active SuperAdmin.</summary>
    private async Task EnsureAnotherActiveSuperAdminAsync(Guid excludingUserId)
    {
        var others = await _context.Users.CountAsync(u =>
            u.Id != excludingUserId && u.IsActive && u.Role.Name == RoleNames.SuperAdmin);
        if (others == 0)
            throw new InvalidStateException("At least one active SuperAdmin must remain.");
    }

    public record CreateUserRequest(
        string FirstName,
        string LastName,
        string Email,
        string Password,
        string RoleId,
        string? DepartmentId
    );
}

public record ChangeRoleRequest(string RoleName);
