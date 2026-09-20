using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>Result summary of a control-center import (section 34).</summary>
public sealed record ControlCenterImportResult
{
    public int ProjectsAdded { get; init; }
    public int ProjectsMatched { get; init; }
    public int RunsAdded { get; init; }
    public int AssetsAdded { get; init; }
    public required IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// Synchronizes the Google Sheet "ZUNAVIO KDP CONTROL CENTER" with PostgreSQL.
/// PostgreSQL is the runtime source of truth; the sheet is the visibility and
/// audit dashboard (section 9). Google failures never break the application.
/// </summary>
public interface IGoogleControlCenterSyncService
{
    /// <summary>Bootstrap import: Projects, AgentRuns, Assets (no duplicates).</summary>
    Task<ControlCenterImportResult> ImportAsync(CancellationToken ct);

    Task SyncProjectAsync(Guid projectId, CancellationToken ct);
    Task SyncAgentRunAsync(Guid runId, CancellationToken ct);
    Task SyncAssetAsync(Guid assetId, CancellationToken ct);
}

/// <summary>
/// Discovers the known prompt Google Docs inside the prompts folder and stores
/// their Drive file IDs on the matching AgentDefinitions.
/// </summary>
public interface IGoogleDriveAgentDefinitionSeeder
{
    Task<int> SeedPromptFileIdsAsync(CancellationToken ct);
}

/// <summary>
/// Resolves runtime factory settings (drive ids, sheet id, prompts folder) from
/// strongly typed options.
/// </summary>
public interface IGoogleConfigurationSettings
{
    string RootFolderId { get; }
    string PromptsFolderId { get; }
    string ControlCenterSpreadsheetId { get; }
    bool GoogleEnabled { get; }
}