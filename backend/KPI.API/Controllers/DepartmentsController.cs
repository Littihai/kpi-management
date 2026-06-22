using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    public DepartmentsController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var departments = await _context.Departments
            .Where(d => d.IsActive)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync();
        return Ok(departments);
    }
}