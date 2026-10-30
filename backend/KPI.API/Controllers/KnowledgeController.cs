using KPI.API.Security;
using KPI.Application.Knowledge;
using KPI.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KPI.API.Controllers;

/// <summary>
/// Reading needs <c>knowledge.view</c>; writing needs <c>knowledge.manage</c>. A draft is a 404 to anyone who cannot manage,
/// decided in the service. There is no anonymous endpoint: the public report form does not suggest articles.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledge;

    public KnowledgeController(IKnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    private Guid UserId => User.GetUserId();

    [HttpGet]
    [Authorize(Policy = Permissions.Knowledge.View)]
    public async Task<IActionResult> List([FromQuery] KnowledgeQuery query)
        => Ok(await _knowledge.ListAsync(query, UserId));

    [HttpGet("categories")]
    [Authorize(Policy = Permissions.Knowledge.View)]
    public async Task<IActionResult> Categories()
        => Ok(await _knowledge.CategoriesAsync(UserId));

    [HttpGet("suggest")]
    [Authorize(Policy = Permissions.Knowledge.View)]
    public async Task<IActionResult> Suggest([FromQuery] string? title)
        => Ok(await _knowledge.SuggestAsync(title, UserId));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Knowledge.View)]
    public async Task<IActionResult> Get(Guid id, [FromQuery] bool countView = true)
        => Ok(await _knowledge.GetAsync(id, UserId, countView));

    [HttpPost]
    [Authorize(Policy = Permissions.Knowledge.Manage)]
    public async Task<IActionResult> Create([FromBody] SaveArticleRequest request)
    {
        var created = await _knowledge.CreateAsync(request, UserId);
        return Created($"/api/knowledge/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Knowledge.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveArticleRequest request)
        => Ok(await _knowledge.UpdateAsync(id, request, UserId));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Permissions.Knowledge.Manage)]
    public async Task<IActionResult> Publish(Guid id)
        => Ok(await _knowledge.PublishAsync(id, UserId));

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = Permissions.Knowledge.Manage)]
    public async Task<IActionResult> Unpublish(Guid id)
        => Ok(await _knowledge.UnpublishAsync(id, UserId));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Knowledge.Manage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _knowledge.DeleteAsync(id, UserId);
        return NoContent();
    }
}
