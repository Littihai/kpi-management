using System.Security.Claims;
using KPI.Infrastructure.Data;
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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.Department)
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

    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsActive = false;
        await _context.SaveChangesAsync();
        return Ok(new { message = "User deactivated" });
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeRoleRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == request.RoleName);
        if (role == null) return BadRequest(new { message = "Role not found" });

        user.RoleId = role.Id;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Role updated" });
    }
}

public record ChangeRoleRequest(string RoleName);