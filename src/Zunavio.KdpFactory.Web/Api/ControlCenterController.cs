using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Text;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Web.Api;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class ControlCenterController : ControllerBase
{
    private const string ApiKeyHeader = "X-Asset-Api-Key";
    private readonly IGoogleControlCenterSyncService _sync;
    private readonly IGoogleDriveAgentDefinitionSeeder _agentSeeder;
    private readonly IConfiguration _configuration;

    public ControlCenterController(
        IGoogleControlCenterSyncService sync,
        IGoogleDriveAgentDefinitionSeeder agentSeeder,
        IConfiguration configuration)
    {
        _sync = sync;
        _agentSeeder = agentSeeder;
        _configuration = configuration;
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        var result = await _sync.ImportAsync(ct);
        return Ok(result);
    }

    [HttpPost("import-projects")]
    public async Task<IActionResult> ImportProjects(CancellationToken ct) => Ok(await _sync.ImportAsync(ct));

    [HttpPost("import-projects-key")]
    [AllowAnonymous]
    public async Task<IActionResult> ImportProjectsWithApiKey(CancellationToken ct)
    {
        if (!IsApiKeyAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        var result = await _sync.ImportAsync(ct);
        return Ok(new
        {
            success = result.Errors.Count == 0,
            projectsAdded = result.ProjectsAdded,
            projectsMatched = result.ProjectsMatched,
            runsAdded = result.RunsAdded,
            assetsAdded = result.AssetsAdded,
            errors = result.Errors
        });
    }

    [HttpPost("seed-agent-prompt-ids")]
    public async Task<IActionResult> SeedPromptFileIds(CancellationToken ct) =>
        Ok(new { matched = await _agentSeeder.SeedPromptFileIdsAsync(ct) });

    private bool IsApiKeyAuthorized()
    {
        var expected = _configuration["ASSET_UPLOAD_API_KEY"];
        var supplied = Request.Headers[ApiKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
            return false;

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}