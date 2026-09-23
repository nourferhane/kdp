using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Google;

/// <summary>Runtime Google factory settings exposed to the rest of the app.</summary>
public sealed class GoogleConfigurationSettings(IOptions<GoogleOptions> options) : IGoogleConfigurationSettings
{
    private readonly GoogleOptions _options = options.Value;

    public string RootFolderId => _options.RootFolderId ?? string.Empty;
    public string PromptsFolderId => _options.PromptsFolderId ?? string.Empty;
    public string ControlCenterSpreadsheetId => _options.ControlCenterSpreadsheetId ?? string.Empty;
    public bool GoogleEnabled => _options.IsConfigured;
}

/// <summary>
/// Synchronizes the ZUNAVIO KDP CONTROL CENTER spreadsheet with PostgreSQL.
/// PostgreSQL is the source of truth; the sheet is read/write for auditing
/// (section 9). Every Google failure is cut off at this boundary.
/// </summary>
public sealed class GoogleControlCenterSyncService : IGoogleControlCenterSyncService
{
    private const string ProjectsTab = "Projects";
    private const string RunsTab = "AgentRuns";
    private const string AssetsTab = "Assets";
    private const string GatesTab = "GateDefinitions";

    private static readonly string[] ProjectsHeaders =
    [
        "projectId", "projectCode", "workingTitle", "finalTitle", "marketplace", "language",
        "targetAge", "bookType", "season", "currentGate", "status", "marketScore",
        "manuscriptVersion", "visualBibleVersion", "productionVersion", "nextAction",
        "driveFolderId", "driveFolderUrl", "updatedAtUtc",
    ];

    private static readonly string[] RunsHeaders =
    [
        "runId", "runCode", "projectCode", "agentCode", "status", "model", "inputVersion",
        "outputVersion", "gateRecommendation", "summary", "startedAtUtc", "completedAtUtc",
        "inputTokens", "outputTokens", "estimatedCost", "error", "idempotencyKey",
    ];

    private static readonly string[] AssetsHeaders =
    [
        "assetId", "assetCode", "projectCode", "assetType", "version", "status",
        "qaStatus", "driveUrl", "createdByRunCode", "createdAtUtc", "driveFileId", "contentJson",
    ];

    private static readonly string[] GatesHeaders = ["order", "gate", "agent"];

    private readonly IGoogleCredentialProvider _credentials;
    private readonly GoogleOptions _options;
    private readonly IUnitOfWork _db;
    private readonly ILogger<GoogleControlCenterSyncService> _logger;

    public GoogleControlCenterSyncService(
        IGoogleCredentialProvider credentials,
        IOptions<GoogleOptions> options,
        IUnitOfWork db,
        ILogger<GoogleControlCenterSyncService> logger)
    {
        _credentials = credentials;
        _options = options.Value;
        _db = db;
        _logger = logger;
    }

    public async Task<ControlCenterImportResult> ImportAsync(CancellationToken ct)
    {
        var result = new ControlCenterImportResult { Errors = [] };
        if (!_options.IsFullyConfigured)
        {
            _logger.LogInformation("Control center not configured; skipping import.");
            result = result with { Errors = ["Google control center is not configured."] };
            return result;
        }

        SheetsService sheets = _credentials.Sheets;
        try
        {
            await EnsureTabsAsync(sheets, ct);

            result = await ImportProjectsAsync(sheets, result, ct);
            result = await ImportRunsAsync(sheets, result, ct);
            result = await ImportAssetsAsync(sheets, result, ct);

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Control center import done: {Projects}+ projects, {Runs}+ runs, {Assets}+ assets ({Errors} errors).",
                result.ProjectsAdded, result.RunsAdded, result.AssetsAdded, result.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Control center import failed.");
            result = result with { Errors = [.. result.Errors, ex.Message] };
        }

        return result;
    }

    public async Task SyncProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(projectId, ct);
        if (project is null) return;

        if (!_options.IsFullyConfigured)
        {
            return;
        }

        try
        {
            // Older Control Centers use Project_ID/Asset_ID columns. Never rewrite
            // their headers or positional data with the newer PostgreSQL schema.
            var headerRequest = _credentials.Sheets.Spreadsheets.Values.Get(
                _options.ControlCenterSpreadsheetId, "Projects!A1");
            var header = await headerRequest.ExecuteAsync(ct);
            if (header.Values?.FirstOrDefault()?.FirstOrDefault()?.ToString() == "Project_ID")
            {
                await SyncLegacyAssetsAsync(project, ct);
                return;
            }

            await EnsureTabsAsync(_credentials.Sheets, ct);
            await WriteProjectRowAsync(project, ct);

            var agentCodes = (await _db.Agents.GetAllAsync(ct)).ToDictionary(a => a.Id, a => a.Code);
            foreach (var run in await _db.Runs.GetByProjectAsync(projectId, ct))
            {
                await WriteRunRowAsync(run, project.ProjectCode, agentCodes.GetValueOrDefault(run.AgentDefinitionId), ct);
            }

            foreach (var asset in await _db.Assets.GetByProjectAsync(projectId, ct))
            {
                await WriteAssetRowAsync(asset, project.ProjectCode, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Control center sync for project {ProjectCode} failed; PostgreSQL remains the source of truth.", project.ProjectCode);
        }
    }

    public Task SyncAgentRunAsync(Guid runId, CancellationToken ct) => Task.CompletedTask;

    public Task SyncAssetAsync(Guid assetId, CancellationToken ct) => Task.CompletedTask;

    private async Task SyncLegacyAssetsAsync(Domain.Entities.Project project, CancellationToken ct)
    {
        var sheets = _credentials.Sheets;
        var response = await sheets.Spreadsheets.Values.Get(
            _options.ControlCenterSpreadsheetId, "Assets!A:I").ExecuteAsync(ct);
        var rows = response.Values ?? [];
        if (rows.Count == 0 || rows[0].Count < 8 || rows[0][0]?.ToString() != "Asset_ID"
            || rows[0][4]?.ToString() != "Drive_URL")
            throw new InvalidOperationException("Unexpected legacy Assets schema; refusing to write.");

        foreach (var asset in await _db.Assets.GetByProjectAsync(project.Id, ct))
        {
            // Only add verified gateway uploads. Leave all existing legacy rows untouched.
            if (asset.AssetType != Domain.Enums.AssetType.Illustration || string.IsNullOrWhiteSpace(asset.DriveFileId))
                continue;
            if (rows.Skip(1).Any(row => row.Count > 0 && row[0]?.ToString() == asset.AssetCode))
                continue;

            var values = new object?[] { asset.AssetCode, project.ProjectCode, "ILLUSTRATION",
                asset.Version, asset.DriveUrl, asset.Status.ToString().ToUpperInvariant(),
                "IMAGE_GENERATION", asset.QaStatus.ToString().ToUpperInvariant(),
                asset.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") };
            var append = sheets.Spreadsheets.Values.Append(new ValueRange
            {
                Values = [values.ToList()]
            }, _options.ControlCenterSpreadsheetId, "Assets!A:I");
            append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            append.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            await append.ExecuteAsync(ct);
        }
    }

    private async Task<ControlCenterImportResult> ImportProjectsAsync(
        SheetsService sheets, ControlCenterImportResult result, CancellationToken ct)
    {
        var header = await GetHeaderAsync(sheets, ProjectsTab, ProjectsHeaders, ct);
        var rows = await ReadDataRowsAsync(sheets, ProjectsTab, ct);
        foreach (var row in rows)
        {
            try
            {
                var key = Value(row, header, "projectId");
                var projectCode = Value(row, header, "projectCode");
                if (string.IsNullOrWhiteSpace(projectCode)) continue;

                var existing = await _db.Projects.GetByCodeAsync(projectCode, ct);
                if (existing is not null)
                {
                    result = result with { ProjectsMatched = result.ProjectsMatched + 1 };
                    continue;
                }

                var gate = Parse<Domain.Enums.ProjectGate>(Value(row, header, "currentGate"));
                var status = Parse<Domain.Enums.ProjectStatus>(Value(row, header, "status"));
                var project = new Domain.Entities.Project
                {
                    ProjectCode = projectCode,
                    WorkingTitle = Value(row, header, "workingTitle") ?? projectCode,
                    FinalTitle = Value(row, header, "finalTitle"),
                    Marketplace = Value(row, header, "marketplace"),
                    Language = Value(row, header, "language"),
                    TargetAge = Value(row, header, "targetAge"),
                    BookType = Value(row, header, "bookType"),
                    Season = Value(row, header, "season"),
                    MarketScore = ParseInt(Value(row, header, "marketScore")),
                    CurrentGate = gate ?? Domain.Enums.ProjectGate.Idea,
                    Status = status ?? Domain.Enums.ProjectStatus.Active,
                    CurrentManuscriptVersion = Value(row, header, "manuscriptVersion"),
                    CurrentVisualBibleVersion = Value(row, header, "visualBibleVersion"),
                    CurrentProductionVersion = Value(row, header, "productionVersion"),
                    NextAction = ParseNextAction(gate, status),
                    DriveFolderId = Value(row, header, "driveFolderId"),
                    DriveFolderUrl = Value(row, header, "driveFolderUrl"),
                    ExternalId = key,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _db.Projects.AddAsync(project, ct);
                result = result with { ProjectsAdded = result.ProjectsAdded + 1 };
            }
            catch (Exception ex)
            {
                result = result with { Errors = [.. result.Errors, $"Projects row: {ex.Message}"] };
            }
        }

        return result;
    }

    private async Task<ControlCenterImportResult> ImportRunsAsync(
        SheetsService sheets, ControlCenterImportResult result, CancellationToken ct)
    {
        var header = await GetHeaderAsync(sheets, RunsTab, RunsHeaders, ct);
        var rows = await ReadDataRowsAsync(sheets, RunsTab, ct);
        foreach (var row in rows)
        {
            try
            {
                var runCode = Value(row, header, "runCode");
                var projectCode = Value(row, header, "projectCode");
                var agentCode = Value(row, header, "agentCode");
                if (string.IsNullOrWhiteSpace(runCode) || string.IsNullOrWhiteSpace(projectCode)) continue;

                if (await _db.Runs.GetByCodeAsync(runCode, ct) is not null) continue;

                var project = await _db.Projects.GetByCodeAsync(projectCode, ct);
                var agent = await _db.Agents.GetByCodeAsync(agentCode ?? string.Empty, ct);
                if (project is null || agent is null)
                {
                    result = result with { Errors = [.. result.Errors, $"Run {runCode}: missing project or agent definition."] };
                    continue;
                }

                await _db.Runs.AddAsync(new Domain.Entities.AgentRun
                {
                    RunCode = runCode,
                    ProjectId = project.Id,
                    AgentDefinitionId = agent.Id,
                    Status = Parse<Domain.Enums.AgentRunStatus>(Value(row, header, "status")) ?? Domain.Enums.AgentRunStatus.Complete,
                    Model = Value(row, header, "model"),
                    InputVersion = Value(row, header, "inputVersion"),
                    OutputVersion = Value(row, header, "outputVersion"),
                    GateRecommendation = Value(row, header, "gateRecommendation"),
                    Summary = Value(row, header, "summary"),
                    StartedAt = ParseDateTime(Value(row, header, "startedAtUtc")),
                    CompletedAt = ParseDateTime(Value(row, header, "completedAtUtc")),
                    InputTokens = ParseInt(Value(row, header, "inputTokens")),
                    OutputTokens = ParseInt(Value(row, header, "outputTokens")),
                    EstimatedCost = ParseDecimal(Value(row, header, "estimatedCost")),
                    ErrorMessage = Value(row, header, "error"),
                    IdempotencyKey = Value(row, header, "idempotencyKey"),
                    CreatedAt = DateTime.UtcNow,
                }, ct);

                result = result with { RunsAdded = result.RunsAdded + 1 };
            }
            catch (Exception ex)
            {
                result = result with { Errors = [.. result.Errors, $"AgentRuns row: {ex.Message}"] };
            }
        }

        return result;
    }

    private async Task<ControlCenterImportResult> ImportAssetsAsync(
        SheetsService sheets, ControlCenterImportResult result, CancellationToken ct)
    {
        var header = await GetHeaderAsync(sheets, AssetsTab, AssetsHeaders, ct);
        var rows = await ReadDataRowsAsync(sheets, AssetsTab, ct);
        foreach (var row in rows)
        {
            try
            {
                var assetCode = Value(row, header, "assetCode");
                var projectCode = Value(row, header, "projectCode");
                if (string.IsNullOrWhiteSpace(assetCode) || string.IsNullOrWhiteSpace(projectCode)) continue;

                if (await _db.Assets.GetByCodeAsync(assetCode, ct) is not null) continue;

                var project = await _db.Projects.GetByCodeAsync(projectCode, ct);
                if (project is null)
                {
                    result = result with { Errors = [.. result.Errors, $"Asset {assetCode}: missing project '{projectCode}'."] };
                    continue;
                }

                var createdByRunCode = Value(row, header, "createdByRunCode");
                var run = createdByRunCode is null ? null : await _db.Runs.GetByCodeAsync(createdByRunCode, ct);

                await _db.Assets.AddAsync(new Domain.Entities.Asset
                {
                    AssetCode = assetCode,
                    ProjectId = project.Id,
                    AssetType = Parse<Domain.Enums.AssetType>(Value(row, header, "assetType")) ?? Domain.Enums.AssetType.Manuscript,
                    Version = Value(row, header, "version") ?? "v1.0",
                    Status = Parse<Domain.Enums.AssetStatus>(Value(row, header, "status")) ?? Domain.Enums.AssetStatus.Draft,
                    QaStatus = Parse<Domain.Enums.AssetQaStatus>(Value(row, header, "qaStatus")) ?? Domain.Enums.AssetQaStatus.NotChecked,
                    DriveFileId = Value(row, header, "driveFileId") ?? ParseDriveIdFromUrl(Value(row, header, "driveUrl")),
                    DriveUrl = Value(row, header, "driveUrl"),
                    CreatedByAgentRunId = run?.Id,
                    ContentJson = Value(row, header, "contentJson") ?? "{}",
                    CreatedAt = DateTime.UtcNow,
                }, ct);

                result = result with { AssetsAdded = result.AssetsAdded + 1 };
            }
            catch (Exception ex)
            {
                result = result with { Errors = [.. result.Errors, $"Assets row: {ex.Message}"] };
            }
        }

        return result;
    }

    private async Task WriteProjectRowAsync(Domain.Entities.Project project, CancellationToken ct)
    {
        var values = new[]
        {
            project.Id.ToString(),
            project.ProjectCode,
            project.WorkingTitle,
            project.FinalTitle,
            project.Marketplace,
            project.Language,
            project.TargetAge,
            project.BookType,
            project.Season,
            project.CurrentGate.ToString(),
            project.Status.ToString(),
            project.MarketScore?.ToString(),
            project.CurrentManuscriptVersion,
            project.CurrentVisualBibleVersion,
            project.CurrentProductionVersion,
            project.NextAction,
            project.DriveFolderId?.ToString(),
            project.DriveFolderUrl,
            project.UpdatedAt.ToUniversalTime().ToString("O"),
        };

        await WriteRowByKeyAsync(ProjectsTab, ProjectsHeaders, values, 0, project.Id.ToString(), ct);
    }

    private async Task WriteRunRowAsync(Domain.Entities.AgentRun run, string projectCode, string? agentCode, CancellationToken ct)
    {
        var values = new[]
        {
            run.Id.ToString(),
            run.RunCode,
            projectCode,
            agentCode ?? string.Empty,
            run.Status.ToString(),
            run.Model,
            run.InputVersion,
            run.OutputVersion,
            run.GateRecommendation,
            run.Summary,
            run.StartedAt?.ToUniversalTime().ToString("O"),
            run.CompletedAt?.ToUniversalTime().ToString("O"),
            run.InputTokens?.ToString(),
            run.OutputTokens?.ToString(),
            run.EstimatedCost?.ToString("G6"),
            run.ErrorMessage,
            run.IdempotencyKey,
        };

        await WriteRowByKeyAsync(RunsTab, RunsHeaders, values, 1, run.RunCode, ct);
    }

    private async Task WriteAssetRowAsync(Domain.Entities.Asset asset, string projectCode, CancellationToken ct)
    {
        var values = new string?[]
        {
            asset.Id.ToString(),
            asset.AssetCode,
            projectCode,
            asset.AssetType.ToString(),
            asset.Version,
            asset.Status.ToString(),
            asset.QaStatus.ToString(),
            asset.DriveUrl,
            null,
            asset.CreatedAt.ToUniversalTime().ToString("O"),
            asset.DriveFileId,
            asset.ContentJson,
        };

        if (asset.CreatedByAgentRunId is not null)
        {
            var run = await _db.Runs.GetByIdAsync(asset.CreatedByAgentRunId.Value, ct);
            values[8] = run?.RunCode;
        }

        await WriteRowByKeyAsync(AssetsTab, AssetsHeaders, values, 1, asset.AssetCode, ct);
    }

    private async Task WriteRowByKeyAsync(
        string tab, string[] headers, string?[] values, int keyColumn, string keyValue, CancellationToken ct)
    {
        SheetsService sheets = _credentials.Sheets;
        var (sheetId, hasData) = await GetTabStateAsync(sheets, tab, headers, ct);
        if (!await EnsureTabAsync(sheets, tab, headers, ct))
        {
            _logger.LogWarning("Could not ensure sheet tab '{Tab}'; sync skipped.", tab);
            return;
        }

        var response = await sheets.Spreadsheets.Values.Get(
            _options.ControlCenterSpreadsheetId, $"{tab}!A:{ColumnLetter(headers.Length)}").ExecuteAsync(ct);

        var rows = response.Values ?? [];
        var rangeStart = 1; // header at index 0
        var rowIndex = -1;
        if (hasData)
        {
            for (var i = 1; i < rows.Count; i++)
            {
                var cells = rows[i];
                if (cells.Count > keyColumn && cells[keyColumn]?.ToString() == keyValue)
                {
                    rowIndex = i;
                    break;
                }
            }
        }

        if (rowIndex == -1)
        {
            rowIndex = Math.Max(rows.Count, 1);
        }

        var rowRange = $"{tab}!A{rowIndex + 1}:{ColumnLetter(values.Length)}{rowIndex + 1}";
        var update = sheets.Spreadsheets.Values.Update(
            new ValueRange
            {
                Range = rowRange,
                Values = [values.Cast<object?>().ToList()],
            },
            _options.ControlCenterSpreadsheetId,
            rowRange);
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await update.ExecuteAsync(ct);
    }

    private async Task<(string? SheetId, bool HasData)> GetTabStateAsync(
        SheetsService sheets, string tab, string[] headers, CancellationToken ct)
    {
        var request = sheets.Spreadsheets.Get(_options.ControlCenterSpreadsheetId);
        request.Fields = "sheets.properties(sheetId,title)";
        var response = await request.ExecuteAsync(ct);

        var sheet = response.Sheets?.FirstOrDefault(s => s.Properties?.Title == tab);
        return (sheet?.Properties?.SheetId?.ToString(), sheet is not null);
    }

    private async Task<bool> EnsureTabAsync(SheetsService sheets, string tab, string[] headers, CancellationToken ct)
    {
        var (_, hasData) = await GetTabStateAsync(sheets, tab, headers, ct);
        if (!hasData)
        {
            var addRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests =
                [
                    new Request
                    {
                        AddSheet = new AddSheetRequest
                        {
                            Properties = new SheetProperties { Title = tab },
                        },
                    },
                ],
            };
            await sheets.Spreadsheets.BatchUpdate(addRequest, _options.ControlCenterSpreadsheetId).ExecuteAsync(ct);
        }

        // Ensure header row.
        var range = $"{tab}!A1:{ColumnLetter(headers.Length)}1";
        var headerUpdate = sheets.Spreadsheets.Values.Update(
            new ValueRange
            {
                Range = range,
                Values = [headers.Cast<object?>().ToList()],
            },
            _options.ControlCenterSpreadsheetId,
            range);
        headerUpdate.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        await headerUpdate.ExecuteAsync(ct);

        return true;
    }

    private async Task<string[]> GetHeaderAsync(SheetsService sheets, string tab, string[] expected, CancellationToken ct)
    {
        await EnsureTabAsync(sheets, tab, expected, ct);
        var response = await sheets.Spreadsheets.Values.Get(
            _options.ControlCenterSpreadsheetId, $"{tab}!A1:{ColumnLetter(expected.Length)}1").ExecuteAsync(ct);
        var header = response.Values is { Count: > 0 } ? response.Values[0].Select(c => c?.ToString() ?? string.Empty).ToArray() : expected;
        return header;
    }

    private async Task<IReadOnlyList<string[]>> ReadDataRowsAsync(
        SheetsService sheets, string tab, CancellationToken ct)
    {
        var response = await sheets.Spreadsheets.Values.Get(
            _options.ControlCenterSpreadsheetId, tab).ExecuteAsync(ct);
        return (response.Values ?? [])
            .Skip(1)
            .Select(row => row.Select(c => c?.ToString()).ToArray())
            .ToList();
    }

    private static string? Value(string[] row, string?[] header, string name)
    {
        var index = Array.IndexOf(header, name);
        return index >= 0 && index < row.Length ? row[index] : null;
    }

    private static T? Parse<T>(string? value) where T : struct, Enum =>
        value is not null && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static int? ParseInt(string? value) => int.TryParse(value, out var i) ? i : null;
    private static decimal? ParseDecimal(string? value) => decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    private static DateTime? ParseDateTime(string? value) =>
        value is not null && DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt.ToUniversalTime() : null;
    private static string? ParseDriveIdFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string marker = "/d/";
        var idx = url.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = url[(idx + marker.Length)..];
        var end = rest.IndexOf('/');
        return end > 0 ? rest[..end] : rest;
    }

    private async Task EnsureTabsAsync(SheetsService sheets, CancellationToken ct)
    {
        await EnsureTabAsync(sheets, ProjectsTab, ProjectsHeaders, ct);
        await EnsureTabAsync(sheets, RunsTab, RunsHeaders, ct);
        await EnsureTabAsync(sheets, AssetsTab, AssetsHeaders, ct);
        await EnsureTabAsync(sheets, GatesTab, GatesHeaders, ct);
    }

    private static string? ParseNextAction(Domain.Enums.ProjectGate? gate, Domain.Enums.ProjectStatus? status)
    {
        if (status is Domain.Enums.ProjectStatus.Paused) return "PAUSED";
        if (status is Domain.Enums.ProjectStatus.Rejected) return "REJECTED";
        if (status is Domain.Enums.ProjectStatus.Completed) return "COMPLETED";
        if (gate is null or Domain.Enums.ProjectGate.None) return null;

        var agent = Domain.Rules.AgentRouting.AgentForGate(gate.Value);
        return $"RUN_{agent.ToString().ToUpperInvariant()}";
    }

    private static string ColumnLetter(int column)
    {
        var result = string.Empty;
        while (column > 0)
        {
            var rem = (column - 1) % 26;
            result = (char)('A' + rem) + result;
            column = (column - 1) / 26;
        }

        return result;
    }
}
