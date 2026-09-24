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
    private readonly IControlCenterProjectImportService _importer;

    public ControlCenterController(
        IGoogleControlCenterSyncService sync,
        IGoogleDriveAgentDefinitionSeeder agentSeeder,
        IConfiguration configuration,
        IControlCenterProjectImportService importer)
    {
        _sync = sync;
        _agentSeeder = agentSeeder;
        _configuration = configuration;
        _importer = importer;
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

    // Import one project from the existing, legacy Control Center schema without
    // rewriting its headers or replacing its workflow state with an IDEA project.
    // Delegates to the same service behind the MCP tool "zunavio_import_project".
    [HttpPost("import-project/{projectCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> ImportProject(string projectCode, CancellationToken ct)
    {
        if (!IsApiKeyAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        var result = await _importer.ImportProjectAsync(projectCode, ct);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "invalid_project_code" => BadRequest(new { success = false, error = result.ErrorCode }),
                "control_center_not_configured" =>
                    StatusCode(503, new { success = false, error = result.ErrorCode }),
                _ => Conflict(new { success = false, error = result.ErrorCode, message = result.ErrorDetail })
            };
        }

        return Ok(new
        {
            success = true,
            created = result.Created,
            projectCode = result.ProjectCode,
            projectId = result.ProjectId,
            gate = result.Gate?.ToString(),
            sourceGate = result.SourceGate,
            sourceStatus = result.SourceStatus,
            databaseStatus = result.DatabaseStatus?.ToString(),
            nextAction = result.NextAction
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