using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.HumanReview;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Application.Projects;
using Zunavio.KdpFactory.Application.Services;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application;

/// <summary>
/// Implements the public orchestrator facade: validates the request, enqueues
/// a durable job and returns. All heavy work happens in the background worker
/// (IOrchestrationEngine) so HTTP requests never wait on a model call.
/// </summary>
public sealed class OrchestratorService : IOrchestratorService
{
    private readonly IUnitOfWork _db;
    private readonly IBackgroundJobManager _jobs;
    private readonly ICodeGenerator _codes;
    private readonly IHumanReviewService _reviews;
    private readonly IGoogleControlCenterSyncService _sync;
    private readonly ILogger<OrchestratorService> _logger;

    public OrchestratorService(
        IUnitOfWork db,
        IBackgroundJobManager jobs,
        ICodeGenerator codes,
        IHumanReviewService reviews,
        IGoogleControlCenterSyncService sync,
        ILogger<OrchestratorService> logger)
    {
        _db = db;
        _jobs = jobs;
        _codes = codes;
        _reviews = reviews;
        _sync = sync;
        _logger = logger;
    }

    public async Task<OrchestratorOperationResult> CreateProjectAsync(CreateProjectRequest request, CancellationToken ct)
    {
        string projectCode = request.ProjectCode?.Trim().ToUpperInvariant() ?? await _codes.NextProjectCodeAsync(ct);

        var existing = await _db.Projects.GetByCodeAsync(projectCode, ct);
        if (existing is not null)
            return OrchestratorOperationResult.Fail($"Project code '{projectCode}' is already in use.");

        var project = new Project
        {
            ProjectCode = projectCode,
            WorkingTitle = request.WorkingTitle,
            Marketplace = string.IsNullOrWhiteSpace(request.Marketplace) ? "Amazon.com" : request.Marketplace,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "English" : request.Language,
            TargetAge = request.TargetAge,
            BookType = request.BookType,
            Season = request.Season,
            CurrentGate = ProjectGate.Idea,
            Status = ProjectStatus.Active,
            NextAction = $"RUN_{AgentCode.Scout.ToString().ToUpperInvariant()}",
            DriveFolderId = request.DriveFolderId,
            ExternalId = request.ExternalId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _db.ExecuteInTransactionAsync(async t =>
        {
            await _db.Projects.AddAsync(project, t);
            await _db.Events.AddAsync(new WorkflowEvent
            {
                ProjectId = project.Id,
                Type = WorkflowEventType.ProjectCreated,
                Title = $"Project {project.ProjectCode} created.",
                NewGate = ProjectGate.Idea,
                CreatedAt = DateTime.UtcNow,
            }, t);
            await _db.SaveChangesAsync(t);
        }, ct);

        return OrchestratorOperationResult.Ok($"Project {project.ProjectCode} created.", projectId: project.Id);
    }

    public async Task<OrchestratorOperationResult> RunNextAsync(Guid projectId, CancellationToken ct) =>
        await EnqueueRunAsync(projectId, null, ct);

    public async Task<OrchestratorOperationResult> RunAgentAsync(Guid projectId, string? agentCode, CancellationToken ct) =>
        await EnqueueRunAsync(projectId, agentCode, ct);

    public async Task<OrchestratorOperationResult> ContinueAsync(Guid projectId, CancellationToken ct) =>
        await EnqueueRunAsync(projectId, null, ct);

    public async Task<OrchestratorOperationResult> ApproveHumanReviewAsync(Guid reviewId, string? comment, string? resolutionPayloadJson, CancellationToken ct) =>
        await _reviews.ApproveAsync(reviewId, comment, resolutionPayloadJson, ct);

    public async Task<OrchestratorOperationResult> RejectHumanReviewAsync(Guid reviewId, string? comment, CancellationToken ct) =>
        await _reviews.RejectAsync(reviewId, comment, ct);

    public async Task<OrchestratorOperationResult> CancelHumanReviewAsync(Guid reviewId, string? comment, CancellationToken ct) =>
        await _reviews.CancelAsync(reviewId, comment, ct);

    public async Task<OrchestratorOperationResult> PauseAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(projectId, ct);
        if (project is null) return OrchestratorOperationResult.Fail("Project not found.");
        if (project.Status is not ProjectStatus.Active)
            return OrchestratorOperationResult.Fail($"Only an ACTIVE project can be paused (current: {project.Status}).");

        await _db.ExecuteInTransactionAsync(async t =>
        {
            project.Status = ProjectStatus.Paused;
            project.UpdatedAt = DateTime.UtcNow;
            project.NextAction = "PAUSED";
            await _db.Events.AddAsync(new WorkflowEvent
            {
                ProjectId = project.Id,
                Type = WorkflowEventType.ProjectPaused,
                Title = $"Project {project.ProjectCode} paused.",
                CreatedAt = DateTime.UtcNow,
            }, t);
            await _db.SaveChangesAsync(t);
        }, ct);

        return OrchestratorOperationResult.Ok("Project paused.", projectId: project.Id);
    }

    public async Task<OrchestratorOperationResult> ResumeAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(projectId, ct);
        if (project is null) return OrchestratorOperationResult.Fail("Project not found.");
        if (project.Status is not ProjectStatus.Paused)
            return OrchestratorOperationResult.Fail($"Only a PAUSED project can be resumed (current: {project.Status}).");

        var pending = await _db.Reviews.GetPendingForProjectAsync(projectId, ct);
        if (pending.Count > 0)
            return OrchestratorOperationResult.Fail("Cannot resume: a blocking human review is still pending.");

        await _db.ExecuteInTransactionAsync(async t =>
        {
            project.Status = ProjectStatus.Active;
            project.UpdatedAt = DateTime.UtcNow;
            project.NextAction = $"RUN_{AgentRoutingFor(project.CurrentGate)}";
            await _db.Events.AddAsync(new WorkflowEvent
            {
                ProjectId = project.Id,
                Type = WorkflowEventType.ProjectResumed,
                Title = $"Project {project.ProjectCode} resumed.",
                CreatedAt = DateTime.UtcNow,
            }, t);
            await _db.SaveChangesAsync(t);
        }, ct);

        return OrchestratorOperationResult.Ok("Project resumed.", projectId: project.Id);
    }

    private async Task<OrchestratorOperationResult> EnqueueRunAsync(Guid projectId, string? agentCode, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(projectId, ct);
        if (project is null) return OrchestratorOperationResult.Fail("Project not found.");

        if (project.Status is not ProjectStatus.Active)
            return OrchestratorOperationResult.Fail($"Project is not ACTIVE (current: {project.Status}).");

        var pendingReviews = await _db.Reviews.GetPendingForProjectAsync(projectId, ct);
        if (pendingReviews.Count > 0)
            return OrchestratorOperationResult.Fail("Cannot run: a blocking human review is pending. Resolve it first.");

        if (project.CurrentGate is ProjectGate.Published or ProjectGate.None)
            return OrchestratorOperationResult.Fail($"Gate '{project.CurrentGate}' has no agent to run.");

        if (agentCode is not null)
        {
            var definition = await _db.Agents.GetByCodeAsync(agentCode, ct);
            if (definition is null) return OrchestratorOperationResult.Fail($"Unknown agent '{agentCode}'.");
            if (!definition.Enabled) return OrchestratorOperationResult.Fail($"Agent '{agentCode}' is disabled.");
        }

        var jobId = await _jobs.EnqueueRunAgentAsync(projectId, agentCode, ct);
        return OrchestratorOperationResult.Ok("Agent run enqueued.", jobId, projectId);
    }

    private static string AgentRoutingFor(ProjectGate gate)
    {
        var code = gate switch
        {
            ProjectGate.Idea or ProjectGate.MarketResearch => AgentCode.Scout,
            ProjectGate.MarketValidation => AgentCode.Validator,
            ProjectGate.Architecture => AgentCode.Architect,
            ProjectGate.Manuscript => AgentCode.Writer,
            ProjectGate.VisualProduction => AgentCode.ArtDirector,
            ProjectGate.BookProduction => AgentCode.Production,
            ProjectGate.Metadata => AgentCode.Metadata,
            ProjectGate.Qa => AgentCode.Qa,
            ProjectGate.ReadyToPublish => AgentCode.Launch,
            _ => throw new ArgumentOutOfRangeException(nameof(gate)),
        };
        return code.ToString().ToUpperInvariant();
    }
}