using KPI.Application.Database;
using KPI.Domain.Constants;
using KPI.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>Is the database complete, and if not what is missing; and creating what is missing without touching what is there.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = Permissions.Settings.View)]
public class DatabaseController : ControllerBase
{
    private readonly AppDbContext _db;

    public DatabaseController(AppDbContext db) => _db = db;

    [HttpGet("status")]
    public async Task<ActionResult<DatabaseReport>> Status(CancellationToken cancellationToken) =>
        await DatabaseDoctor.InspectAsync(_db, cancellationToken);

    /// <summary>What a repair would do, with the SQL for missing tables and columns, so it can be read first.</summary>
    [HttpGet("repair/preview")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<ActionResult<RepairPreview>> Preview(CancellationToken cancellationToken) =>
        await DatabaseDoctor.PreviewAsync(_db, cancellationToken);

    [HttpPost("repair")]
    [Authorize(Policy = Permissions.Settings.Manage)]
    public async Task<ActionResult<RepairResult>> Repair([FromBody] RepairRequest request, CancellationToken cancellationToken) =>
        await DatabaseDoctor.RepairAsync(_db, request, cancellationToken);
}
