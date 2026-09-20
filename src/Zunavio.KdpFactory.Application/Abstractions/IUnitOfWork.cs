using System.Linq.Expressions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// Repository boundary. Implementations are EF Core based and live in
/// Infrastructure; the control plane only depends on these contracts.
/// </summary>
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct);
    Task<Project?> GetByExternalIdAsync(string externalId, CancellationToken ct);
    Task AddAsync(Project project, CancellationToken ct);
    Task<int> GetProjectSequenceAsync(CancellationToken ct);
    Task<IReadOnlyList<Project>> GetActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<Project>> WithPendingReviewsAsync(CancellationToken ct);
}

public interface IAgentDefinitionRepository
{
    Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken ct);
    Task<AgentDefinition?> GetByAgentCodeAsync(AgentCode code, CancellationToken ct);
    Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct);
    Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(AgentDefinition definition, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<AgentDefinition> definitions, CancellationToken ct);
}

public interface IAgentRunRepository
{
    Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<AgentRun?> GetByCodeAsync(string runCode, CancellationToken ct);
    Task<IReadOnlyList<AgentRun>> GetByProjectAsync(Guid projectId, CancellationToken ct);
    Task<AgentRun?> GetLatestForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct);
    Task<AgentRun?> GetLatestCompleteForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct);
    Task<bool> HasActiveDuplicateAsync(string idempotencyKey, CancellationToken ct);
    Task<AgentRun?> GetLatestForKeyAsync(string idempotencyKey, CancellationToken ct);
    Task AddAsync(AgentRun run, CancellationToken ct);
    Task<int> GetRunSequenceAsync(Guid projectId, CancellationToken ct);
}

public interface IAssetRepository
{
    Task<IReadOnlyList<Asset>> GetByProjectAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<Asset>> GetApprovedByTypeAsync(Guid projectId, AssetType type, CancellationToken ct);
    Task<IReadOnlyList<Asset>> GetLatestApprovedForProjectAsync(Guid projectId, CancellationToken ct);
    Task<Asset?> GetLatestApprovedAsync(Guid projectId, AssetType type, CancellationToken ct);
    Task<Asset?> GetLatestAsync(Guid projectId, AssetType type, CancellationToken ct);
    Task<Asset?> GetByCodeAsync(string assetCode, CancellationToken ct);
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Asset asset, CancellationToken ct);
    Task<IReadOnlyList<Asset>> GetAllAsync(CancellationToken ct);
}

public interface IHumanReviewRepository
{
    Task<HumanReviewRequest?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<HumanReviewRequest>> GetPendingForProjectAsync(Guid projectId, CancellationToken ct);
    Task<IReadOnlyList<HumanReviewRequest>> GetPendingAsync(CancellationToken ct);
    Task<IReadOnlyList<HumanReviewRequest>> GetByProjectAsync(Guid projectId, CancellationToken ct);
    Task AddAsync(HumanReviewRequest request, CancellationToken ct);
}

public interface IWorkflowEventRepository
{
    Task AddAsync(WorkflowEvent workflowEvent, CancellationToken ct);
    Task<IReadOnlyList<WorkflowEvent>> GetByProjectAsync(Guid projectId, CancellationToken ct);
}

public interface IBackgroundJobRepository
{
    Task AddAsync(BackgroundJob job, CancellationToken ct);
    Task<BackgroundJob?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsActiveByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);
    Task<BackgroundJob?> ClaimNextAsync(CancellationToken ct);
    Task<int> RequeueStaleAsync(int leaseSeconds, CancellationToken ct);
    Task MarkCompletedAsync(Guid id, CancellationToken ct);
    Task MarkFailedAsync(Guid id, string error, int maxAttempts, CancellationToken ct);
    Task MarkCancelledAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<BackgroundJob>> GetRecentAsync(int take, CancellationToken ct);
}

/// <summary>Coordinates all persistence through a single transaction boundary.</summary>
public interface IUnitOfWork
{
    IProjectRepository Projects { get; }
    IAgentDefinitionRepository Agents { get; }
    IAgentRunRepository Runs { get; }
    IAssetRepository Assets { get; }
    IHumanReviewRepository Reviews { get; }
    IWorkflowEventRepository Events { get; }
    IBackgroundJobRepository Jobs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct);
}