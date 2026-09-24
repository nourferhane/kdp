using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Rules;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Google;

/// <summary>
/// Reads a single legacy "ZUNAVIO KDP CONTROL CENTER" project row and imports
/// it into PostgreSQL. Backs both the REST route
/// <c>POST /api/controlcenter/import-project/{code}</c> and the MCP tool
/// <c>zunavio_import_project</c> so the two entry points share one code path.
///
/// Idempotency: a project already present (by ProjectCode) is returned as-is and
/// never re-imported or overwritten. The sheet is read-only (headers untouched).
/// </summary>
public class ControlCenterProjectImportService : IControlCenterProjectImportService
{
    private static readonly Regex ProjectCodePattern = new("^ZNV-[0-9]{3,}$", RegexOptions.Compiled);

    private readonly IGoogleCredentialProvider _credentials;
    private readonly GoogleOptions _options;
    private readonly IUnitOfWork _db;
    private readonly ILogger<ControlCenterProjectImportService> _logger;

    public ControlCenterProjectImportService(
        IGoogleCredentialProvider credentials,
        IOptions<GoogleOptions> options,
        IUnitOfWork db,
        ILogger<ControlCenterProjectImportService> logger)
    {
        _credentials = credentials;
        _options = options.Value;
        _db = db;
        _logger = logger;
    }

    public async Task<ProjectImportResult> ImportProjectAsync(string projectCode, CancellationToken ct)
    {
        projectCode = projectCode.Trim().ToUpperInvariant();
        if (!ProjectCodePattern.IsMatch(projectCode))
        {
            return ProjectImportResult.Fail("invalid_project_code", projectCode, "Expected a legacy code such as ZNV-002 or ZNV-005.");
        }

        // Idempotent replay: the project already lives in PostgreSQL.
        var existing = await _db.Projects.GetByCodeAsync(projectCode, ct);
        if (existing is not null)
        {
            return new ProjectImportResult
            {
                Success = true,
                Created = false,
                ProjectCode = existing.ProjectCode,
                ProjectId = existing.Id,
                Gate = existing.CurrentGate,
                DatabaseStatus = existing.Status,
                NextAction = existing.NextAction,
            };
        }

        if (!_options.IsFullyConfigured || string.IsNullOrWhiteSpace(_options.ControlCenterSpreadsheetId))
        {
            return ProjectImportResult.Fail(
                "control_center_not_configured",
                projectCode,
                "GOOGLE_* credentials and GOOGLE_CONTROL_CENTER_SPREADSHEET_ID are required.");
        }

        var rows = await ReadSheetRowsSafelyAsync(ct);
        if (rows is null)
        {
            return ProjectImportResult.Fail(
                "google_sheets_unavailable",
                projectCode,
                "The Google Sheets backend could not be reached (API disabled, permission or network error).");
        }

        if (rows.Count == 0 || rows[0].Count == 0)
        {
            return ProjectImportResult.Fail(
                "unexpected_control_center_schema",
                projectCode,
                "Expected a legacy Projects tab header containing 'Project_ID'.");
        }

        var headers = RowHeaders(rows[0]);
        if (!headers.Contains("Project_ID"))
        {
            return ProjectImportResult.Fail(
                "unexpected_control_center_schema",
                projectCode,
                "Expected a legacy Projects tab header containing 'Project_ID'.");
        }

        string CellRow(int rowIndex, string header)
        {
            var index = headers.IndexOf(header);
            var cells = rows[rowIndex];
            return index >= 0 && index < cells.Count ? cells[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;
        }

        var matches = Enumerable.Range(1, rows.Count - 1)
            .Where(i => string.Equals(CellRow(i, "Project_ID"), projectCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return ProjectImportResult.Fail("project_not_in_control_center", projectCode, "No row with this Project_ID in the Projects tab.");
        }

        if (matches.Count > 1)
        {
            return ProjectImportResult.Fail("duplicate_project_rows", projectCode, "Several rows share this Project_ID; refusing to guess.");
        }

        var row = matches[0];
        var sourceGate = CellRow(row, "Current_Gate");
        var sourceStatus = CellRow(row, "Status");
        var (gate, databaseStatus) = ControlCenterLegacyMapper.MapState(
            sourceGate,
            sourceStatus,
            CellRow(row, "Manuscript_Version"),
            CellRow(row, "Visual_Bible_Version"),
            CellRow(row, "Production_Version"));

        // Preserve the existing project folder when the sheet link is usable.
        var folderUrl = CellRow(row, "Project_Folder_URL");
        var folderId = ControlCenterLegacyMapper.TryParseDriveFolderId(folderUrl);
        if (folderId is null)
        {
            return ProjectImportResult.Fail(
                "invalid_project_folder_url",
                projectCode,
                $"Project_Folder_URL is not a /drive/folders/ URL: '{folderUrl}'.");
        }

        var project = new Project
        {
            ProjectCode = projectCode,
            WorkingTitle = Clip(CellRow(row, "Working_Title"), 200),
            FinalTitle = Clip(CellRow(row, "Final_Title"), 200),
            Marketplace = Clip(CellRow(row, "Marketplace"), 64),
            Language = Clip(CellRow(row, "Language"), 32),
            TargetAge = Clip(CellRow(row, "Target_Age"), 32),
            BookType = Clip(CellRow(row, "Book_Type"), 64),
            Season = Clip(CellRow(row, "Season"), 64),
            CurrentGate = gate,
            Status = databaseStatus,
            MarketScore = int.TryParse(CellRow(row, "Market_Score"), out var score) ? score : null,
            CurrentManuscriptVersion = Clip(CellRow(row, "Manuscript_Version"), 16),
            CurrentVisualBibleVersion = Clip(CellRow(row, "Visual_Bible_Version"), 16),
            CurrentProductionVersion = Clip(CellRow(row, "Production_Version"), 16),
            QaResult = CellRow(row, "QA_Result"),
            NextAction = Clip(ControlCenterLegacyMapper.NextAction(
                sourceGate, sourceStatus, CellRow(row, "Next_Action"),
                CellRow(row, "Manuscript_Version"), CellRow(row, "Visual_Bible_Version"), CellRow(row, "Production_Version")), 64),
            DriveFolderId = folderId,
            DriveFolderUrl = folderUrl,
        };

        await _db.Projects.AddAsync(project, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Imported legacy Control Center project {ProjectCode} (gate {Gate}, status {Status}).",
            projectCode, gate, databaseStatus);

        return new ProjectImportResult
        {
            Success = true,
            Created = true,
            ProjectCode = projectCode,
            ProjectId = project.Id,
            Gate = gate,
            DatabaseStatus = databaseStatus,
            SourceGate = sourceGate,
            SourceStatus = sourceStatus,
            NextAction = project.NextAction,
        };
    }

    /// <summary>
    /// Reads every row of the legacy Projects tab, mapping reachability failures
    /// (disabled API, bad permissions, network) to a stable result instead of a 500.
    /// Injected seam for tests: subclasses can return a fixed table without touching Google.
    /// </summary>
    protected virtual async Task<IList<IList<object>>?> ReadSheetRowsAsync(CancellationToken ct)
    {
        var request = _credentials.Sheets.Spreadsheets.Values.Get(
            _options.ControlCenterSpreadsheetId, "Projects!A:T");
        var range = await request.ExecuteAsync(ct);
        return range.Values;
    }

    private async Task<IList<IList<object>>?> ReadSheetRowsSafelyAsync(CancellationToken ct)
    {
        try
        {
            return await ReadSheetRowsAsync(ct);
        }
        catch (global::Google.GoogleApiException ex)
        {
            _logger.LogWarning(ex, "Google Sheets is not reachable for the Control Center import.");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network failure while reading the Control Center sheet.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException)
        {
            _logger.LogWarning(ex, "I/O failure while reading the Control Center sheet.");
        }

        return null;
    }

    /// <summary>Header words of the first (header) row.</summary>
    private static List<string> RowHeaders(IList<object> headerRow) =>
        headerRow.Select(v => (v?.ToString() ?? string.Empty).Trim().TrimStart('\ufeff')).ToList();

    private static string Clip(string? value, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }
}