using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Web.Api;

[ApiController]
[Route("api/[controller]")]
public sealed class ControlCenterController : ControllerBase
{
    private readonly IGoogleControlCenterSyncService _sync;
    private readonly IGoogleDriveAgentDefinitionSeeder _agentSeeder;

    public ControlCenterController(IGoogleControlCenterSyncService sync, IGoogleDriveAgentDefinitionSeeder agentSeeder)
    {
        _sync = sync;
        _agentSeeder = agentSeeder;
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        var result = await _sync.ImportAsync(ct);
        return Ok(result);
    }

    [HttpPost("import-projects")]
    public async Task<IActionResult> ImportProjects(CancellationToken ct) => Ok(await _sync.ImportAsync(ct));

    [HttpPost("seed-agent-prompt-ids")]
    public async Task<IActionResult> SeedPromptFileIds(CancellationToken ct) =>
        Ok(new { matched = await _agentSeeder.SeedPromptFileIdsAsync(ct) });
}