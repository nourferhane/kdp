using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Application.Projects;

namespace Zunavio.KdpFactory.Application;

/// <summary>
/// Public facade of the control plane. Never blocks on long AI executions:
/// it enqueues work and returns immediately (section 18).
/// </summary>
public interface IOrchestratorService
{
    Task<OrchestratorOperationResult> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct);
    Task<OrchestratorOperationResult> RunNextAsync(Guid projectId, CancellationToken ct);
    Task<OrchestratorOperationResult> RunAgentAsync(Guid projectId, string? agentCode, CancellationToken ct);
    Task<OrchestratorOperationResult> ContinueAsync(Guid projectId, CancellationToken ct);
    Task<OrchestratorOperationResult> ApproveHumanReviewAsync(Guid reviewId, string? comment, string? resolutionPayloadJson, CancellationToken ct);
    Task<OrchestratorOperationResult> RejectHumanReviewAsync(Guid reviewId, string? comment, CancellationToken ct);
    Task<OrchestratorOperationResult> CancelHumanReviewAsync(Guid reviewId, string? comment, CancellationToken ct);
    Task<OrchestratorOperationResult> PauseAsync(Guid projectId, CancellationToken ct);
    Task<OrchestratorOperationResult> ResumeAsync(Guid projectId, CancellationToken ct);
}

/// <summary>Read-only queries for the dashboard and API.</summary>
public interface IProjectQueryService
{
    Task<IReadOnlyList<ProjectSummaryDto>> GetAllAsync(CancellationToken ct);
    Task<ProjectDetailDto?> GetAsync(Guid id, CancellationToken ct);
}

public interface IAgentDefinitionQueryService
{
    Task<IReadOnlyList<AgentDefinitionDto>> GetAllAsync(CancellationToken ct);
}

public interface IReviewQueryService
{
    Task<IReadOnlyList<ReviewDto>> GetPendingAsync(CancellationToken ct);
    Task<IReadOnlyList<ReviewDto>> GetByProjectAsync(Guid projectId, CancellationToken ct);
}

public interface IJobQueryService
{
    Task<IReadOnlyList<JobDto>> GetRecentAsync(int take, CancellationToken ct);
}