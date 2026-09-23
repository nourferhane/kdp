using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Configuration;
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
    private readonly IGoogleCredentialProvider _google;
    private readonly IUnitOfWork _db;
    private readonly GoogleOptions _googleOptions;

    public ControlCenterController(
        IGoogleControlCenterSyncService sync,
        IGoogleDriveAgentDefinitionSeeder agentSeeder,
        IConfiguration configuration,
        IGoogleCredentialProvider google,
        IUnitOfWork db,
        IOptions<GoogleOptions> googleOptions)
    {
        _sync = sync;
        _agentSeeder = agentSeeder;
        _configuration = configuration;
        _google = google;
        _db = db;
        _googleOptions = googleOptions.Value;
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
    [HttpPost("import-project/{projectCode}")]
    [AllowAnonymous]
    public async Task<IActionResult> ImportProject(string projectCode, CancellationToken ct)
    {
        if (!IsApiKeyAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        projectCode = projectCode.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(projectCode, "^ZNV-[0-9]{3,}$"))
            return BadRequest(new { success = false, error = "invalid_project_code" });

        var existing = await _db.Projects.GetByCodeAsync(projectCode, ct);
        if (existing is not null)
            return Ok(new { success = true, created = false, projectCode, projectId = existing.Id });

        if (string.IsNullOrWhiteSpace(_googleOptions.ControlCenterSpreadsheetId))
            return StatusCode(503, new { success = false, error = "control_center_not_configured" });

        var response = await _google.Sheets.Spreadsheets.Values.Get(
            _googleOptions.ControlCenterSpreadsheetId, "Projects!A:S").ExecuteAsync(ct);
        var rows = response.Values;
        if (rows is null || rows.Count == 0 || rows[0].Count == 0 || rows[0][0]?.ToString() != "Project_ID")
            return Conflict(new { success = false, error = "unexpected_control_center_schema" });

        var headers = rows[0].Select(v => v?.ToString() ?? "").ToList();
        string Cell(IList<object> row, string header)
        {
            var index = headers.IndexOf(header);
            return index >= 0 && index < row.Count ? row[index]?.ToString()?.Trim() ?? "" : "";
        }

        var matches = rows.Skip(1).Where(row =>
            string.Equals(Cell(row, "Project_ID"), projectCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            return Conflict(new { success = false, error = matches.Count == 0 ? "project_not_in_control_center" : "duplicate_project_rows" });

        var source = matches[0];
        var gate = Cell(source, "Current_Gate");
        if (!Enum.TryParse<ProjectGate>(gate.Replace("_", ""), true, out var parsedGate) || parsedGate == ProjectGate.None)
            return Conflict(new { success = false, error = "unsupported_gate", gate });

        var sourceStatus = Cell(source, "Status");
        var status = sourceStatus switch
        {
            "BLOCKED_NEEDS_HUMAN" or "PAUSED" => ProjectStatus.Paused,
            "REJECTED" => ProjectStatus.Rejected,
            "READY_TO_PUBLISH" or "PUBLISHED" => ProjectStatus.Completed,
            "ACTIVE" or "RUNNING_AUTONOMOUS" => ProjectStatus.Active,
            _ => (ProjectStatus?)null
        };
        if (status is null)
            return Conflict(new { success = false, error = "unsupported_status", status = sourceStatus });

        var folderUrl = Cell(source, "Project_Folder_URL");
        var folderId = Uri.TryCreate(folderUrl, UriKind.Absolute, out var uri)
            && uri.Host == "drive.google.com" && uri.AbsolutePath.StartsWith("/drive/folders/", StringComparison.Ordinal)
            ? uri.AbsolutePath["/drive/folders/".Length..].Trim('/') : null;
        if (folderId is null)
            return Conflict(new { success = false, error = "invalid_project_folder_url" });

        var project = new Project
        {
            ProjectCode = projectCode,
            WorkingTitle = Cell(source, "Working_Title"),
            FinalTitle = Cell(source, "Final_Title"),
            Marketplace = Cell(source, "Marketplace"),
            Language = Cell(source, "Language"),
            TargetAge = Cell(source, "Target_Age"),
            BookType = Cell(source, "Book_Type"),
            Season = Cell(source, "Season"),
            CurrentGate = parsedGate,
            Status = status.Value,
            MarketScore = int.TryParse(Cell(source, "Market_Score"), out var score) ? score : null,
            CurrentManuscriptVersion = Cell(source, "Manuscript_Version"),
            CurrentVisualBibleVersion = Cell(source, "Visual_Bible_Version"),
            CurrentProductionVersion = Cell(source, "Production_Version"),
            QaResult = Cell(source, "QA_Result"),
            NextAction = Cell(source, "Next_Action"),
            DriveFolderId = folderId,
            DriveFolderUrl = folderUrl,
        };
        await _db.Projects.AddAsync(project, ct);
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true, created = true, projectCode, projectId = project.Id,
            gate = project.CurrentGate.ToString(), sourceStatus, databaseStatus = project.Status.ToString() });
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
