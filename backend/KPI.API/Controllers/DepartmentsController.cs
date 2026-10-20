using KPI.Application.Admin;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Department.View)]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IOrgAdminService _org;

    public DepartmentsController(AppDbContext context, IOrgAdminService org)
    {
        _context = context;
        _org = org;
    }

    /// <summary>Active departments as id and name, for pickers.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _context.ListActiveAsync());
    }

    /// <summary>Every department with its head and head-count, inactive ones included (the admin list).</summary>
    [HttpGet("all")]
    [Authorize(Policy = Permissions.Department.Manage)]
    public async Task<IActionResult> ListAll() => Ok(await _org.ListDepartmentsAsync());

    [HttpPost]
    [Authorize(Policy = Permissions.Department.Manage)]
    public async Task<IActionResult> Create([FromBody] SaveDepartmentRequest request)
    {
        var created = await _org.CreateDepartmentAsync(request);
        return Created($"/api/departments/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Department.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveDepartmentRequest request)
        => Ok(await _org.UpdateDepartmentAsync(id, request));
}
