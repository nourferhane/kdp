using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Web.Mcp;

/// <summary>
/// MCP tools for Zunavio projects. The endpoint /mcp requires a valid OAuth
/// Bearer token (Auth0) carrying the mcp:tools scope; mutating tools like
/// <c>zunavio_import_project</c> additionally require the mcp:tools:write scope.
/// </summary>
[McpServerToolType]
[Authorize(Policy = McpSecurity.McpToolsPolicy)]
public sealed class ZunavioProjectTools(IControlCenterProjectImportService importer, IUnitOfWork db)
{
    [McpServerTool(Name = "zunavio_import_project"),
     Description("Import a project from the Control Center spreadsheet into PostgreSQL. Idempotent: a project already present is never re-imported or overwritten, and the spreadsheet is never modified.")]
    [Authorize(Policy = McpSecurity.McpWritePolicy)]
    public async Task<string> ImportProjectAsync(
        [Description("Legacy project code, for example ZNV-005.")] string projectCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectCode))
        {
            return JsonSerializer.Serialize(new { success = false, error = "project_code_required" });
        }

        var result = await importer.ImportProjectAsync(projectCode, ct);
        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            created = result.Created,
            error = result.Success ? (string?)null : result.ErrorCode,
            errorDetail = result.ErrorDetail,
            projectCode = result.ProjectCode,
            projectId = result.ProjectId,
            gate = result.Gate?.ToString(),
            databaseStatus = result.DatabaseStatus?.ToString(),
            sourceGate = result.SourceGate,
            sourceStatus = result.SourceStatus,
            nextAction = result.NextAction
        });
    }

    [McpServerTool(Name = "zunavio_get_project"),
     Description("Get the current state and next action of a Zunavio project from PostgreSQL.")]
    public async Task<string> GetProjectAsync(
        [Description("Project code, for example ZNV-002.")] string projectCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectCode))
        {
            return JsonSerializer.Serialize(new { success = false, error = "project_code_required" });
        }

        // Read from PostgreSQL through the same unit of work as the REST API.
        var project = await db.Projects.GetByCodeAsync(projectCode.Trim(), ct);
        if (project is null)
        {
            return JsonSerializer.Serialize(new { success = false, error = "project_not_found", projectCode = projectCode.Trim() });
        }

        return JsonSerializer.Serialize(new
        {
            success = true,
            projectCode = project.ProjectCode,
            workingTitle = project.WorkingTitle,
            gate = project.CurrentGate.ToString(),
            status = project.Status.ToString(),
            nextAction = string.IsNullOrWhiteSpace(project.NextAction) ? null : project.NextAction,
            marketScore = project.MarketScore,
            driveFolderUrl = project.DriveFolderUrl
        });
    }
}