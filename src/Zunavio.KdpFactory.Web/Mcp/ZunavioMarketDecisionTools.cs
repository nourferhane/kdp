using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Apis.Sheets.v4.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Web.Mcp;

/// <summary>
/// Narrow, evidence-backed transitions for a rejected Scout opportunity. A recommendation
/// and an independent Orchestrator decision require separate persisted Drive documents.
/// </summary>
[McpServerToolType]
[Authorize(Policy = McpSecurity.McpToolsPolicy)]
public sealed class ZunavioMarketDecisionTools(
    IUnitOfWork db, IArtifactStorage storage, IGoogleCredentialProvider google,
    IGoogleConfigurationSettings settings, ILogger<ZunavioMarketDecisionTools> logger)
{
    [McpServerTool(Name = "zunavio_record_scout_rejection"),
     Description("Record a Scout rejection recommendation from an existing, verified project Google Doc. Requires an active MarketResearch project and synchronized Control Center/PostgreSQL state. Does not reject or archive the project.")]
    [Authorize(Policy = McpSecurity.McpWritePolicy)]
    public Task<string> RecordScoutRejectionAsync(
        [Description("Existing project code, e.g. ZNV-006.")] string projectCode,
        [Description("Real Google Doc file ID in a subfolder of this project.")] string driveFileId,
        [Description("Document version, e.g. v1.2.")] string version,
        CancellationToken ct) => ApplyAsync(projectCode, driveFileId, version, finalize: false, ct);

    [McpServerTool(Name = "zunavio_finalize_scout_rejection"),
     Description("Record a separate Orchestrator rejection review from a verified project Google Doc. Requires a previously recorded Scout rejection recommendation; archives the project without publishing.")]
    [Authorize(Policy = McpSecurity.McpWritePolicy)]
    public Task<string> FinalizeScoutRejectionAsync(
        [Description("Existing project code, e.g. ZNV-006.")] string projectCode,
        [Description("Real, separate Orchestrator review Google Doc file ID in this project.")] string driveFileId,
        [Description("Review document version, e.g. v1.0.")] string version,
        CancellationToken ct) => ApplyAsync(projectCode, driveFileId, version, finalize: true, ct);

    private async Task<string> ApplyAsync(string projectCode, string driveFileId, string version,
        bool finalize, CancellationToken ct)
    {
        projectCode = projectCode?.Trim() ?? "";
        driveFileId = driveFileId?.Trim() ?? "";
        version = version?.Trim() ?? "";
        if (!Regex.IsMatch(projectCode, @"^ZNV-\d{3,}$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(driveFileId, @"^[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(version, @"^v\d+\.\d+$", RegexOptions.CultureInvariant))
            return Result(false, "invalid_input", projectCode);
        if (!settings.GoogleEnabled || string.IsNullOrWhiteSpace(settings.ControlCenterSpreadsheetId))
            return Result(false, "control_center_unavailable", projectCode);

        var expectedNext = finalize ? "REVIEW_SCOUT_DECISION" : "";
        var expectedQa = finalize ? "SCOUT_REJECTION_RECOMMENDED" : "";
        var next = finalize ? "NONE" : "REVIEW_SCOUT_DECISION";
        var qa = finalize ? "SCOUT_FAIL_UNPROVEN_DEMAND" : "SCOUT_REJECTION_RECOMMENDED";
        var assetType = finalize ? AssetType.OrchestratorReview : AssetType.ScoutReport;
        var prefix = finalize ? "ORCH" : "SCOUT";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(driveFileId)))[..8];
        var assetCode = $"{projectCode}-{prefix}-{suffix}";
        try
        {
            var project = await db.Projects.GetByCodeAsync(projectCode, ct);
            if (project is null) return Result(false, "project_not_found", projectCode);
            if (finalize ? !Allowed(project, expectedNext, expectedQa)
                : !IsScoutRecommendationState(project))
                return Result(false, "project_state_changed", projectCode);
            if (!finalize)
            {
                // Carry the exact approved starting state into both the sheet check and
                // the pre-write database recheck. Never accept an arbitrary action pair.
                expectedNext = project.NextAction;
                expectedQa = project.QaResult;
            }
            if ((await db.Reviews.GetPendingForProjectAsync(project.Id, ct)).Count != 0)
                return Result(false, "human_review_pending", projectCode);

            var folderId = project.DriveFolderId
                ?? ControlCenterLegacyMapper.TryParseDriveFolderId(project.DriveFolderUrl);
            if (string.IsNullOrWhiteSpace(folderId))
                return Result(false, "project_folder_missing", projectCode);
            var fileRequest = google.Drive.Files.Get(driveFileId);
            fileRequest.Fields = "id,name,mimeType,parents,trashed";
            var file = await fileRequest.ExecuteAsync(ct);
            if (file.Id != driveFileId || file.Trashed == true
                || file.MimeType != "application/vnd.google-apps.document"
                || file.Parents is not { Count: 1 })
                return Result(false, "invalid_evidence_file", projectCode);
            var parentRequest = google.Drive.Files.Get(file.Parents[0]);
            parentRequest.Fields = "id,mimeType,parents,trashed";
            var parent = await parentRequest.ExecuteAsync(ct);
            if (parent.Trashed == true || parent.MimeType != "application/vnd.google-apps.folder"
                || parent.Parents is null || !parent.Parents.Contains(folderId))
                return Result(false, "evidence_outside_project_folder", projectCode);
            var document = await storage.ReadDocumentAsync(driveFileId, ct);
            if (document.FileId != driveFileId
                || !document.Content.Contains(projectCode, StringComparison.Ordinal)
                || !(finalize
                    ? document.Content.Contains("SCOUT_FAIL_UNPROVEN_DEMAND", StringComparison.OrdinalIgnoreCase)
                    : ContainsScoutRejectionRecommendation(document.Content)))
                return Result(false, "evidence_content_mismatch", projectCode);
            if (finalize)
            {
                var scout = (await db.Assets.GetByProjectAsync(project.Id, ct))
                    .Where(a => a.AssetType == AssetType.ScoutReport && a.DriveFileId != driveFileId)
                    .Any(a => a.Notes == "SCOUT_REJECTION_RECOMMENDED");
                if (!scout) return Result(false, "scout_evidence_not_registered", projectCode);
            }
            if (await db.Assets.GetByCodeAsync(assetCode, ct) is not null)
                return Result(false, "evidence_already_registered", projectCode);

            var sheets = google.Sheets;
            var spreadsheetId = settings.ControlCenterSpreadsheetId;
            var response = await sheets.Spreadsheets.Values.Get(spreadsheetId, "Projects!A:S").ExecuteAsync(ct);
            var rows = response.Values;
            if (rows is null || rows.Count < 2) return Result(false, "control_center_missing", projectCode);
            var header = rows[0].Select(x => x?.ToString() ?? "").ToArray();
            var required = new[] { "Project_ID", "Current_Gate", "Status", "QA_Result", "Next_Action", "Updated_At" };
            if (required.Any(k => !header.Contains(k)))
                return Result(false, "control_center_schema_mismatch", projectCode);
            var matches = rows.Select((row, index) => (row, index))
                .Where(pair => Cell(pair.row, Array.IndexOf(header, "Project_ID")) == projectCode).ToArray();
            if (matches.Length != 1) return Result(false, "control_center_project_not_unique", projectCode);
            var (original, index) = matches[0];
            if (Cell(original, Array.IndexOf(header, "Current_Gate")) != "MARKET_RESEARCH"
                || Cell(original, Array.IndexOf(header, "Status")) != "ACTIVE"
                || Cell(original, Array.IndexOf(header, "Next_Action")) != expectedNext
                || Cell(original, Array.IndexOf(header, "QA_Result")) != expectedQa)
                return Result(false, "control_center_state_changed", projectCode);

            // Recheck immediately before the first mutation; EF's row-version protects the DB write.
            var latest = await db.Projects.GetTrackedByCodeAsync(projectCode, ct);
            if (latest is null || latest.Id != project.Id
                || !Allowed(latest, expectedNext, expectedQa))
                return Result(false, "project_state_changed", projectCode);
            var rowNumber = index + 1;
            var changes = new Dictionary<string, string>
            {
                ["QA_Result"] = qa,
                ["Next_Action"] = next,
                ["Updated_At"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            if (finalize)
            {
                changes["Current_Gate"] = "REJECTED";
                changes["Status"] = "ARCHIVED";
            }
            var previous = changes.Keys.ToDictionary(k => k, k => Cell(original, Array.IndexOf(header, k)));
            try
            {
                await WriteCellsAsync(spreadsheetId, header, rowNumber, changes, ct);
                var after = await sheets.Spreadsheets.Values.Get(spreadsheetId,
                    $"Projects!A{rowNumber}:S{rowNumber}").ExecuteAsync(ct);
                if (after.Values?.FirstOrDefault() is not { } readback
                    || changes.Any(kv => Cell(readback, Array.IndexOf(header, kv.Key)) != kv.Value))
                    throw new InvalidOperationException("Control Center read-back failed.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Control Center decision write failed for {ProjectCode}", projectCode);
                try { await RestoreIfOwnedAsync(spreadsheetId, header, rowNumber, changes, previous, ct); }
                catch (Exception restoreError)
                {
                    logger.LogError(restoreError, "Control Center decision rollback failed for {ProjectCode}", projectCode);
                    return Result(false, "reconciliation_required", projectCode);
                }
                return Result(false, "control_center_verification_failed", projectCode);
            }

            try
            {
                latest.QaResult = qa;
                latest.NextAction = next;
                if (finalize) latest.Status = ProjectStatus.Rejected;
                latest.UpdatedAt = DateTime.UtcNow;
                await db.Assets.AddAsync(new Asset
                {
                    ProjectId = latest.Id, AssetCode = assetCode, AssetType = assetType,
                    Version = version, DriveFileId = driveFileId,
                    DriveUrl = $"https://docs.google.com/document/d/{driveFileId}/edit",
                    Status = AssetStatus.Draft, QaStatus = AssetQaStatus.NotChecked,
                    Notes = qa,
                    ContentJson = JsonSerializer.Serialize(new { decision = qa, evidenceFileId = driveFileId })
                }, ct);
                await db.Events.AddAsync(new WorkflowEvent
                {
                    ProjectId = latest.Id,
                    Type = finalize ? WorkflowEventType.ProjectRejected : WorkflowEventType.AssetCreated,
                    Title = qa, Description = $"Verified evidence: {driveFileId}"
                }, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database decision write failed for {ProjectCode}", projectCode);
                try { await RestoreIfOwnedAsync(spreadsheetId, header, rowNumber, changes, previous, ct); }
                catch (Exception restoreError)
                {
                    logger.LogError(restoreError, "Decision rollback failed for {ProjectCode}", projectCode);
                    return Result(false, "reconciliation_required", projectCode);
                }
                return Result(false, "database_write_failed", projectCode);
            }
            var persisted = await db.Projects.GetByCodeAsync(projectCode, ct);
            var evidence = await db.Assets.GetByCodeAsync(assetCode, ct);
            var finalSheet = await sheets.Spreadsheets.Values.Get(spreadsheetId,
                $"Projects!A{rowNumber}:S{rowNumber}").ExecuteAsync(ct);
            if (persisted?.QaResult != qa || persisted.NextAction != next
                || persisted.Status != (finalize ? ProjectStatus.Rejected : ProjectStatus.Active)
                || evidence?.DriveFileId != driveFileId
                || finalSheet.Values?.FirstOrDefault() is not { } finalRow
                || changes.Any(kv => Cell(finalRow, Array.IndexOf(header, kv.Key)) != kv.Value))
                return Result(false, "reconciliation_required", projectCode);
            return JsonSerializer.Serialize(new { success = true, projectCode, gate = "MARKET_RESEARCH",
                status = finalize ? "REJECTED" : "ACTIVE", qaResult = qa, nextAction = next,
                assetCode, driveFileId, controlCenterRow = rowNumber,
                databaseVerified = true, controlCenterVerified = true, driveVerified = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Market decision failed for {ProjectCode}; verify both systems", projectCode);
            return Result(false, "decision_failed_check_both_systems", projectCode);
        }
    }

    public static bool Allowed(Project project, string expectedNext, string expectedQa) =>
        project.CurrentGate == ProjectGate.MarketResearch && project.Status == ProjectStatus.Active
        && project.NextAction == expectedNext && project.QaResult == expectedQa;

    public static bool IsScoutRecommendationState(Project project) =>
        Allowed(project, "RUN_SCOUT_TARGETED_EVIDENCE", "VALIDATOR_HOLD")
        || Allowed(project, "RUN_SCOUT_CONTINUE", "SCOUT_IN_PROGRESS")
        || Allowed(project, "RUN_SCOUT_CONTINUE", "RESEARCH_IN_PROGRESS");

    public static bool ContainsScoutRejectionRecommendation(string content) =>
        content.Contains("SCOUT_REJECTION_RECOMMENDED", StringComparison.OrdinalIgnoreCase)
        || content.Contains("REJECTION RECOMMENDED", StringComparison.OrdinalIgnoreCase)
        || content.Contains("rejet", StringComparison.OrdinalIgnoreCase);

    private async Task RestoreIfOwnedAsync(string spreadsheetId, string[] header, int rowNumber,
        Dictionary<string, string> changes, Dictionary<string, string> previous, CancellationToken ct)
    {
        var current = await google.Sheets.Spreadsheets.Values.Get(spreadsheetId,
            $"Projects!A{rowNumber}:S{rowNumber}").ExecuteAsync(ct);
        var row = current.Values?.FirstOrDefault()
            ?? throw new InvalidOperationException("Cannot verify row ownership before rollback.");
        if (changes.Any(kv =>
        {
            var value = Cell(row, Array.IndexOf(header, kv.Key));
            return value != kv.Value && value != previous[kv.Key];
        }))
            throw new InvalidOperationException("Control Center row changed concurrently; refusing rollback.");
        await WriteCellsAsync(spreadsheetId, header, rowNumber, previous, ct);
    }

    private async Task WriteCellsAsync(string spreadsheetId, string[] header, int rowNumber,
        Dictionary<string, string> values, CancellationToken ct)
    {
        var data = values.Select(kv => new ValueRange
        {
            Range = $"Projects!{Column(Array.IndexOf(header, kv.Key) + 1)}{rowNumber}",
            Values = [new List<object> { kv.Value }]
        }).ToList();
        await google.Sheets.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest
        {
            Data = data, ValueInputOption = "RAW"
        }, spreadsheetId).ExecuteAsync(ct);
    }

    private static string Cell(IList<object> row, int index) =>
        index < 0 || index >= row.Count ? "" : row[index]?.ToString() ?? "";
    private static string Column(int n)
    {
        var s = "";
        while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; }
        return s;
    }
    private static string Result(bool success, string error, string projectCode) =>
        JsonSerializer.Serialize(new { success, error, projectCode });
}
