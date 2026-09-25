using System.ComponentModel;
using System.Text.Json;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Web.Mcp;

/// <summary>Explicit, guarded recovery of a human-blocked visual-production project.</summary>
[McpServerToolType]
[Authorize(Policy = McpSecurity.McpToolsPolicy)]
public sealed class ZunavioRecoveryTools(
    IUnitOfWork db, IGoogleCredentialProvider google, IGoogleConfigurationSettings settings)
{
    [McpServerTool(Name = "zunavio_resume_visual_production"),
     Description("Resume an existing paused visual-production project only when the legacy Control Center still says BLOCKED_NEEDS_HUMAN. Sets next action to image generation, never visual QA or completed.")]
    [Authorize(Policy = McpSecurity.McpWritePolicy)]
    public async Task<string> ResumeVisualProductionAsync(
        [Description("Existing project code, for example ZNV-002.")] string projectCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectCode))
            return Result(false, "project_code_required", projectCode);
        projectCode = projectCode.Trim();
        var project = await db.Projects.GetByCodeAsync(projectCode, ct);
        if (project is null) return Result(false, "project_not_found", projectCode);
        if (project.CurrentGate != ProjectGate.VisualProduction || project.Status != ProjectStatus.Paused)
            return Result(false, "project_not_paused_in_visual_production", projectCode);
        if ((await db.Reviews.GetPendingForProjectAsync(project.Id, ct)).Count != 0)
            return Result(false, "human_review_pending", projectCode);
        if (!settings.GoogleEnabled || string.IsNullOrWhiteSpace(settings.ControlCenterSpreadsheetId))
            return Result(false, "control_center_unavailable", projectCode);

        try
        {
            var sheets = google.Sheets;
            var spreadsheetId = settings.ControlCenterSpreadsheetId;
            var response = await sheets.Spreadsheets.Values.Get(spreadsheetId, "Projects!A:S").ExecuteAsync(ct);
            var rows = response.Values;
            if (rows is null || rows.Count < 2)
                return Result(false, "control_center_rows_missing", projectCode);
            var header = rows[0].Select(v => v?.ToString() ?? "").ToArray();
            var required = new[] { "Project_ID", "Current_Gate", "Status", "QA_Result", "Next_Action", "Updated_At" };
            if (required.Any(name => !header.Contains(name)))
                return Result(false, "control_center_schema_mismatch", projectCode);
            var idIndex = Array.IndexOf(header, "Project_ID");
            var matching = rows.Select((row, index) => (row, index))
                .Where(pair => Cell(pair.row, idIndex) == projectCode).ToArray();
            if (matching.Length != 1)
                return Result(false, matching.Length == 0 ? "control_center_project_not_found" : "duplicate_control_center_project", projectCode);
            var (original, index) = matching[0];
            var gate = Cell(original, Array.IndexOf(header, "Current_Gate"));
            var status = Cell(original, Array.IndexOf(header, "Status"));
            var nextAction = Cell(original, Array.IndexOf(header, "Next_Action"));
            if (gate != "VISUAL_PRODUCTION" || status != "BLOCKED_NEEDS_HUMAN"
                || nextAction != "HUMAN_INTERVENTION_REQUIRED")
                return Result(false, "control_center_state_changed", projectCode);

            // Recheck PostgreSQL immediately before mutating either system.
            var latest = await db.Projects.GetTrackedByCodeAsync(projectCode, ct);
            if (latest is null || latest.Id != project.Id || latest.Status != ProjectStatus.Paused
                || latest.CurrentGate != ProjectGate.VisualProduction)
                return Result(false, "project_state_changed", projectCode);
            var rowNumber = index + 1;
            var changes = new Dictionary<string, string>
            {
                ["Status"] = "ACTIVE", ["QA_Result"] = "PENDING_IMAGE_GENERATION",
                ["Next_Action"] = "RUN_IMAGE_GENERATION", ["Updated_At"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var previous = changes.Keys.ToDictionary(key => key,
                key => Cell(original, Array.IndexOf(header, key)));
            await WriteCellsAsync(spreadsheetId, header, rowNumber, changes, ct);
            var after = await sheets.Spreadsheets.Values.Get(spreadsheetId,
                $"Projects!A{rowNumber}:S{rowNumber}").ExecuteAsync(ct);
            var readback = after.Values?.FirstOrDefault();
            if (readback is null || changes.Any(kv => Cell(readback, Array.IndexOf(header, kv.Key)) != kv.Value))
            {
                await WriteCellsAsync(spreadsheetId, header, rowNumber, previous, ct);
                return Result(false, "control_center_verification_failed", projectCode);
            }

            try
            {
                latest.Status = ProjectStatus.Active;
                latest.NextAction = "RUN_IMAGE_GENERATION";
                latest.QaResult = "PENDING_IMAGE_GENERATION";
                latest.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                var persisted = await db.Projects.GetByCodeAsync(projectCode, ct);
                if (persisted is null || persisted.Status != ProjectStatus.Active
                    || persisted.CurrentGate != ProjectGate.VisualProduction
                    || persisted.NextAction != "RUN_IMAGE_GENERATION"
                    || persisted.QaResult != "PENDING_IMAGE_GENERATION")
                    throw new InvalidOperationException("PostgreSQL recovery read-back did not match the requested state.");
            }
            catch
            {
                await WriteCellsAsync(spreadsheetId, header, rowNumber, previous, ct);
                throw;
            }
            return JsonSerializer.Serialize(new { success = true, projectCode, gate = "VISUAL_PRODUCTION",
                status = "ACTIVE", qaResult = "PENDING_IMAGE_GENERATION", nextAction = "RUN_IMAGE_GENERATION",
                controlCenterRow = rowNumber, databaseVerified = true, controlCenterVerified = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = "recovery_failed",
                projectCode, detail = ex.GetBaseException().Message,
                warning = "Check both PostgreSQL and Control Center before retrying; a partial write may require manual reconciliation." });
        }
    }

    private async Task WriteCellsAsync(string spreadsheetId, string[] header, int rowNumber,
        Dictionary<string, string> values, CancellationToken ct)
    {
        var data = values.Select(kv => new ValueRange
        {
            Range = $"Projects!{Column(Array.IndexOf(header, kv.Key) + 1)}{rowNumber}",
            Values = [new List<object> { kv.Value }]
        }).ToList();
        var request = google.Sheets.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest
        {
            Data = data, ValueInputOption = "RAW"
        }, spreadsheetId);
        await request.ExecuteAsync(ct);
    }

    private static string Cell(IList<object> row, int index) => index < 0 || index >= row.Count
        ? string.Empty : row[index]?.ToString() ?? string.Empty;
    private static string Column(int oneBased)
    {
        var name = "";
        while (oneBased > 0) { oneBased--; name = (char)('A' + oneBased % 26) + name; oneBased /= 26; }
        return name;
    }
    private static string Result(bool success, string error, string projectCode) =>
        JsonSerializer.Serialize(new { success, error, projectCode });
}
