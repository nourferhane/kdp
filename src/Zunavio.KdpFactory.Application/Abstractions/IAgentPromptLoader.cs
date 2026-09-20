using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>Snapshot state of an agent prompt loaded from Google Drive.</summary>
public sealed record PromptSnapshot
{
    public required string Text { get; init; }
    public required string Version { get; init; }
    public required string Hash { get; init; }
    public required string DriveFileId { get; init; }
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Loads the authoritative prompt for an agent from Google Drive and caches
/// it in the AgentDefinition table. Drive is the authoring source; the
/// database only mirrors it.
/// </summary>
public interface IAgentPromptLoader
{
    /// <summary>
    /// Resolves the current prompt for an agent. Returns the cached copy when
    /// either Drive is unavailable or the Drive hash has not changed.
    /// </summary>
    Task<PromptSnapshot> LoadAsync(AgentDefinition definition, CancellationToken ct);

    /// <summary>Fetches the prompt fresh from Drive and refreshes the cache.</summary>
    Task<PromptSnapshot> RefreshAsync(AgentDefinition definition, CancellationToken ct);
}

/// <summary>UI-facing refresh operation for a single agent prompt.</summary>
public interface IAgentPromptRefreshService
{
    Task<PromptSnapshot> RefreshAsync(Guid agentDefinitionId, CancellationToken ct);
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct);
}