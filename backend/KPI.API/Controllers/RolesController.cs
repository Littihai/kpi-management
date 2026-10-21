using KPI.API.Security;
using KPI.Application.Admin;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Role.View)]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IRoleAdminService _roles;

    public RolesController(AppDbContext context, IRoleAdminService roles)
    {
        _context = context;
        _roles = roles;
    }

    /// <summary>Id and name of every role, for pickers.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _context.Roles
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();
        return Ok(roles);
    }

    /// <summary>Every role with its permissions, scope and head-count (the admin list and the matrix).</summary>
    [HttpGet("details")]
    public async Task<IActionResult> Details() => Ok(await _roles.ListAsync());

    /// <summary>All permissions with their plain-language meaning.</summary>
    [HttpGet("permissions")]
    public IActionResult Catalog() => Ok(_roles.Catalog());

    [HttpPost]
    [Authorize(Policy = Permissions.Role.Manage)]
    public async Task<IActionResult> Create([FromBody] SaveRoleRequest request)
    {
        var created = await _roles.CreateAsync(User.GetUserId(), request);
        return Created($"/api/roles/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Role.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveRoleRequest request)
        => Ok(await _roles.UpdateAsync(User.GetUserId(), id, request));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Role.Manage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _roles.DeleteAsync(User.GetUserId(), id);
        return NoContent();
    }
}
