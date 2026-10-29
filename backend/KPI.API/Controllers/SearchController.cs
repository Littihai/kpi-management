using KPI.API.Security;
using KPI.Application.Issues;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly IQuickSearchService _search;

    public SearchController(IQuickSearchService search)
    {
        _search = search;
    }

    /// <summary>The Ctrl+K box: a few issues, and people for whoever may open the user list.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Issue.View)]
    public async Task<IActionResult> Quick([FromQuery] string? q)
        => Ok(await _search.SearchAsync(q, User.GetUserId()));
}
