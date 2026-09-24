using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Application.Tests;

public class ControlCenterProjectImportServiceTests
{
    private static readonly string[] Header =
        ["Project_ID", "Current_Gate", "Status", "Manuscript_Version", "Visual_Bible_Version",
         "Production_Version", "Project_Folder_URL", "Working_Title", "Final_Title", "Marketplace",
         "Language", "Target_Age", "Book_Type", "Season", "Market_Score", "QA_Result", "Next_Action"];

    private static List<IList<object>> Sheet(params IList<object>[] rows)
    {
        var table = new List<IList<object>> { Header };
        table.AddRange(rows);
        return table;
    }

    private static IList<object> Row(string code, string gate, string status, string folderUrl,
        string? manuscript = null, string? visual = null, string? production = null,
        string? title = "Working Title", string? score = null) =>
        [code, gate, status, manuscript!, visual!, production!, folderUrl, title, null!,
         "Amazon.com", "English", "6-8", "Picture", "Spring", score!, null!, null!];

    private sealed class Service(GoogleOptions options, IUnitOfWork db) : ControlCenterProjectImportService(
        new ThrowingCredentialProvider(),
        Options.Create(options),
        db,
        NullLogger<ControlCenterProjectImportService>.Instance)
    {
        public IList<IList<object>>? Rows { get; set; }

        protected override Task<IList<IList<object>>?> ReadSheetRowsAsync(CancellationToken ct) =>
            Task.FromResult(Rows);
    }

    private sealed class ThrowingCredentialProvider : IGoogleCredentialProvider
    {
        public bool IsConfigured => throw new NotSupportedException();
        public Google.Apis.Auth.OAuth2.GoogleCredential Credential => throw new NotSupportedException();
        public Google.Apis.Http.IConfigurableHttpClientInitializer Initializer => throw new NotSupportedException();
        public Google.Apis.Drive.v3.DriveService Drive => throw new NotSupportedException();
        public Google.Apis.Docs.v1.DocsService Docs => throw new NotSupportedException();
        public Google.Apis.Sheets.v4.SheetsService Sheets => throw new NotSupportedException();
    }

    private static ControlCenterProjectImportService Build(
        FakeUnitOfWork db, IList<IList<object>>? rows, bool configured = true)
    {
        var options = new GoogleOptions
        {
            ServiceAccountJson = configured ? "{}" : null,
            ControlCenterSpreadsheetId = configured ? "spreadsheet-123" : null,
        };
        return new Service(options, db) { Rows = rows };
    }

    [Theory]
    [InlineData("FOO")]
    [InlineData("ZNV-12")]
    [InlineData("znv")]
    [InlineData(" zebra ")]
    public async Task ImportProjectAsync_rejects_invalid_codes(string code)
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, null);

        var result = await service.ImportProjectAsync(code, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_project_code", result.ErrorCode);
        Assert.Empty(db.Projects.Projects);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task ImportProjectAsync_fails_cleanly_when_google_sheets_is_unreachable()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, null);

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("google_sheets_unavailable", result.ErrorCode);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task ImportProjectAsync_fails_when_google_is_not_configured()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(), configured: false);

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("control_center_not_configured", result.ErrorCode);
    }

    [Fact]
    public async Task ImportProjectAsync_replays_existing_project_without_touching_the_sheet()
    {
        var db = new FakeUnitOfWork();
        var existing = new Domain.Entities.Project
        {
            Id = Guid.NewGuid(),
            ProjectCode = "ZNV-002",
            CurrentGate = ProjectGate.MarketResearch,
            Status = ProjectStatus.Active,
            NextAction = "RUN_SCOUT",
        };
        db.Projects.Projects.Add(existing);

        // The sheet is never read: missing rows prove the early-exit path.
        var service = Build(db, null);

        var result = await service.ImportProjectAsync("znv-002", CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Created);
        Assert.Equal(existing.Id, result.ProjectId);
        Assert.Equal(ProjectGate.MarketResearch, result.Gate);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task ImportProjectAsync_rejects_a_sheet_without_the_expected_header()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, [new object[] { "ID", "Gate" }]);

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("unexpected_control_center_schema", result.ErrorCode);
    }

    [Fact]
    public async Task ImportProjectAsync_fails_when_the_row_is_missing()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet());

        var result = await service.ImportProjectAsync("ZNV-009", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("project_not_in_control_center", result.ErrorCode);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task ImportProjectAsync_fails_on_duplicate_rows_for_the_same_code()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(
            Row("ZNV-002", "MARKET_RESEARCH", "ACTIVE", "https://drive.google.com/drive/folders/f1"),
            Row("ZNV-002", "ARCHITECTURE", "ACTIVE", "https://drive.google.com/drive/folders/f2")));

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("duplicate_project_rows", result.ErrorCode);
    }

    [Fact]
    public async Task ImportProjectAsync_fails_when_the_folder_url_is_not_a_drive_folder()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(Row("ZNV-002", "MARKET_RESEARCH", "ACTIVE", "https://example.com/x")));

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("invalid_project_folder_url", result.ErrorCode);
    }

    [Fact]
    public async Task ImportProjectAsync_imports_an_active_row()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(Row(
            "ZNV-002", "MARKET_RESEARCH", "ACTIVE",
            "https://drive.google.com/drive/folders/folder-abc", score: "78")));

        var result = await service.ImportProjectAsync("ZNV-002", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Created);
        Assert.Equal(ProjectGate.MarketResearch, result.Gate);
        Assert.Equal(ProjectStatus.Active, result.DatabaseStatus);
        Assert.Equal("MARKET_RESEARCH", result.SourceGate);
        Assert.Equal("ACTIVE", result.SourceStatus);
        Assert.Equal("RUN_SCOUT", result.NextAction);
        Assert.Equal(1, db.SaveChangesCalls);

        var project = Assert.Single(db.Projects.Projects);
        Assert.Equal("ZNV-002", project.ProjectCode);
        Assert.Equal(ProjectGate.MarketResearch, project.CurrentGate);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal("folder-abc", project.DriveFolderId);
        Assert.Equal("Working Title", project.WorkingTitle);
        Assert.Equal(78, project.MarketScore);
    }

    [Fact]
    public async Task ImportProjectAsync_imports_row_with_leading_index_column_and_quoted_csv_commas()
    {
        var db = new FakeUnitOfWork();
        var header = new object[] { "", "Project_ID", "Current_Gate", "Status", "Manuscript_Version", "Visual_Bible_Version",
            "Production_Version", "Project_Folder_URL", "Working_Title", "Final_Title", "Marketplace",
            "Language", "Target_Age", "Book_Type", "Season", "Market_Score", "QA_Result", "Next_Action" };
        var row = new object[] { "5", "ZNV-010", "REJECTED", "ARCHIVED", "", "", "",
            "https://drive.google.com/drive/folders/folder-10", "My Digital Life Emergency Planner", "",
            "United States primary; United Kingdom, Canada, Australia secondary", "English", "Adults; families; caregivers",
            "Guided digital-legacy and emergency-access organizer", "Evergreen", "67", "SCOUT_FAIL_UNPROVEN_DEMAND", "NONE" };
        var service = Build(db, [header, row]);

        var result = await service.ImportProjectAsync("ZNV-010", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Created);
        Assert.Equal(ProjectStatus.Rejected, result.DatabaseStatus);
        Assert.Equal("NONE", result.NextAction);

        var project = Assert.Single(db.Projects.Projects);
        Assert.Equal("ZNV-010", project.ProjectCode);
        Assert.Equal("United States primary; United Kingdom, Canada, Australia secon", project.Marketplace);
        Assert.Equal(64, project.Marketplace.Length);
        Assert.Equal(67, project.MarketScore);
    }

    [Fact]
    public async Task ImportProjectAsync_imports_a_rejected_row_using_current_gate_state()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(Row(
            "ZNV-005", "REJECTED", "ACTIVE",
            "https://drive.google.com/drive/folders/folder-xyz",
            manuscript: "v0.3", visual: "v0.1")));

        var result = await service.ImportProjectAsync("ZNV-005", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Created);
        // REJECTED lives in Current_Gate: the status column is overridden and the
        // gate is inferred from the version columns.
        Assert.Equal(ProjectStatus.Rejected, result.DatabaseStatus);
        Assert.Equal(ProjectGate.VisualProduction, result.Gate);
        Assert.Equal("REJECTED", result.NextAction);

        var project = Assert.Single(db.Projects.Projects);
        Assert.Equal(ProjectStatus.Rejected, project.Status);
        Assert.Equal(ProjectGate.VisualProduction, project.CurrentGate);
        Assert.Equal("v0.3", project.CurrentManuscriptVersion);
        Assert.Equal("v0.1", project.CurrentVisualBibleVersion);
    }

    [Fact]
    public async Task ImportProjectAsync_does_not_gas_the_sheet_nor_reenumerate_when_already_saved()
    {
        var db = new FakeUnitOfWork();
        var service = Build(db, Sheet(Row(
            "ZNV-006", "READY_TO_PUBLISH", "ACTIVE",
            "https://drive.google.com/drive/folders/folder-6")));

        var first = await service.ImportProjectAsync("ZNV-006", CancellationToken.None);
        var replay = await service.ImportProjectAsync("znv-006", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.ProjectId, replay.ProjectId);
        Assert.Single(db.Projects.Projects);
        Assert.Equal(1, db.SaveChangesCalls);
    }
}