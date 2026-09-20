using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Web.Api;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IAgentDefinitionQueryService _agents;
    private readonly IAgentPromptRefreshService _refresh;

    public AgentsController(IAgentDefinitionQueryService agents, IAgentPromptRefreshService refresh)
    {
        _agents = agents;
        _refresh = refresh;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await _agents.GetAllAsync(ct));

    [HttpPost("{id:guid}/refresh-prompt")]
    public async Task<IActionResult> RefreshPrompt(Guid id, CancellationToken ct)
    {
        try
        {
            var snapshot = await _refresh.RefreshAsync(id, ct);
            return Ok(new { version = snapshot.Version, hash = snapshot.Hash, driveFileId = snapshot.DriveFileId });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}