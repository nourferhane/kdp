using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Tests;

/// <summary>In-memory repositories for unit tests. Only the members used by the
/// services under test are implemented; everything else throws so a regression
/// that adds a repository access silently no-ops instead of failing the test.</summary>
public sealed class FakeProjectRepository : IProjectRepository
{
    public List<Project> Projects { get; } = [];

    public Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.ProjectCode == projectCode));

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Project>>(Projects.ToList());

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

    public Task AddAsync(Project project, CancellationToken ct)
    {
        Projects.Add(project);
        return Task.CompletedTask;
    }

    public Task<Project?> GetByExternalIdAsync(string externalId, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> GetProjectSequenceAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Project>> GetActiveAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Project>> WithPendingReviewsAsync(CancellationToken ct) => throw new NotSupportedException();
}

public sealed class FakeAgentRepository : IAgentDefinitionRepository
{
    public List<AgentDefinition> Agents { get; } = [];

    public Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentDefinition>>(Agents.ToList());

    public Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentDefinition?> GetByAgentCodeAsync(AgentCode code, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task AddAsync(AgentDefinition definition, CancellationToken ct) => throw new NotSupportedException();
    public Task AddRangeAsync(IEnumerable<AgentDefinition> definitions, CancellationToken ct) => throw new NotSupportedException();
}

public sealed class FakeAgentRunRepository : IAgentRunRepository
{
    public List<AgentRun> Runs { get; } = [];

    public Task<IReadOnlyList<AgentRun>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AgentRun>>(Runs.Where(r => r.ProjectId == projectId).ToList());

    public Task AddAsync(AgentRun run, CancellationToken ct)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentRun?> GetByCodeAsync(string runCode, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentRun?> GetLatestForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentRun?> GetLatestCompleteForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasActiveDuplicateAsync(string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
    public Task<AgentRun?> GetLatestForKeyAsync(string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> GetRunSequenceAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
}

public sealed class FakeAssetRepository : IAssetRepository
{
    public List<Asset> Assets { get; } = [];

    public Task<IReadOnlyList<Asset>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Asset>>(Assets.Where(a => a.ProjectId == projectId).ToList());

    public Task<IReadOnlyList<Asset>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Asset>>(Assets.ToList());

    public Task AddAsync(Asset asset, CancellationToken ct)
    {
        Assets.Add(asset);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Asset>> GetApprovedByTypeAsync(Guid projectId, AssetType type, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Asset>> GetLatestApprovedForProjectAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
    public Task<Asset?> GetLatestApprovedAsync(Guid projectId, AssetType type, CancellationToken ct) => throw new NotSupportedException();
    public Task<Asset?> GetLatestAsync(Guid projectId, AssetType type, CancellationToken ct) => throw new NotSupportedException();
    public Task<Asset?> GetByCodeAsync(string assetCode, CancellationToken ct) => throw new NotSupportedException();
    public Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
}

public sealed class FakeReviewRepository : IHumanReviewRepository
{
    public List<HumanReviewRequest> Requests { get; } = [];

    public Task<IReadOnlyList<HumanReviewRequest>> GetPendingForProjectAsync(Guid projectId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HumanReviewRequest>>(
            Requests.Where(r => r.ProjectId == projectId && r.Status == HumanReviewStatus.Pending).ToList());

    public Task AddAsync(HumanReviewRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }

    public Task<HumanReviewRequest?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<HumanReviewRequest>> GetPendingAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<HumanReviewRequest>> GetByProjectAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>In-memory IUnitOfWork wired to a full set of fake repositories.
/// Events and background jobs remain unsupported (throw).</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public FakeProjectRepository Projects { get; } = new();
    public FakeAgentRepository Agents { get; } = new();
    public FakeAgentRunRepository Runs { get; } = new();
    public FakeAssetRepository Assets { get; } = new();
    public FakeReviewRepository Reviews { get; } = new();
    public int SaveChangesCalls { get; private set; }

    IProjectRepository IUnitOfWork.Projects => Projects;
    IAgentDefinitionRepository IUnitOfWork.Agents => Agents;
    IAgentRunRepository IUnitOfWork.Runs => Runs;
    IAssetRepository IUnitOfWork.Assets => Assets;
    IHumanReviewRepository IUnitOfWork.Reviews => Reviews;
    IWorkflowEventRepository IUnitOfWork.Events => throw new NotSupportedException();
    IBackgroundJobRepository IUnitOfWork.Jobs => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        throw new NotSupportedException();
    }
}