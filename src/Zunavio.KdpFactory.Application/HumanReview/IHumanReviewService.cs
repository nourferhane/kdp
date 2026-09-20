using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Application.Services;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Application.HumanReview;

/// <summary>
/// Resolves a blocking human review and applies the deferred gate action.
/// Only the orchestrator may change gates — this service is part of the
/// orchestrator and applies validated, deterministic transitions only.
/// </summary>
public interface IHumanReviewService
{
    Task<OrchestratorOperationResult> ApproveAsync(Guid reviewId, string? comment, string? resolutionPayloadJson, CancellationToken ct);
    Task<OrchestratorOperationResult> RejectAsync(Guid reviewId, string? comment, CancellationToken ct);
    Task<OrchestratorOperationResult> CancelAsync(Guid reviewId, string? comment, CancellationToken ct);
}

public sealed class HumanReviewService : IHumanReviewService
{
    private readonly IUnitOfWork _db;
    private readonly IBackgroundJobManager _jobs;
    private readonly IWorkflowTransitionValidator _transitions;
    private readonly IGoogleControlCenterSyncService _sync;
    private readonly ILogger<HumanReviewService> _logger;

    public HumanReviewService(
        IUnitOfWork db,
        IBackgroundJobManager jobs,
        IWorkflowTransitionValidator transitions,
        IGoogleControlCenterSyncService sync,
        ILogger<HumanReviewService> logger)
    {
        _db = db;
        _jobs = jobs;
        _transitions = transitions;
        _sync = sync;
        _logger = logger;
    }

    public async Task<OrchestratorOperationResult> ApproveAsync(Guid reviewId, string? comment, string? resolutionPayloadJson, CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["ReviewId"] = reviewId });

        var review = await _db.Reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return OrchestratorOperationResult.Fail("Review not found.");

        if (review.Status != HumanReviewStatus.Pending)
            return OrchestratorOperationResult.Fail($"Review is already {review.Status}.");

        var project = review.Project
            ?? await _db.Projects.GetByIdAsync(review.ProjectId, ct)
            ?? throw new InvalidOperationException($"Project {review.ProjectId} missing.");

        var payload = ReviewPayload.Parse(review.PayloadJson);

        try
        {
            await _db.ExecuteInTransactionAsync(async t =>
            {
                review.Status = HumanReviewStatus.Approved;
                review.ResolvedAt = DateTime.UtcNow;
                review.ResolutionComment = comment;
                review.ResolutionPayloadJson = resolutionPayloadJson;

                await ApplyApprovalAsync(project, review, payload, resolutionPayloadJson, t);
                await _db.SaveChangesAsync(t);
            }, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Cannot approve review {ReviewId}.", review.Id);
            return OrchestratorOperationResult.Fail(ex.Message);
        }

        await AddEventSafeAsync(project.Id, WorkflowEventType.HumanReviewApproved, reviewId, $"Review approved: {review.Title}", comment, ct);

        // Sync is best-effort; PostgreSQL remains the truth.
        try { await _sync.SyncProjectAsync(project.Id, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Control center sync failed after review approval."); }

        await MaybeContinueAsync(project, review, ct);

        return OrchestratorOperationResult.Ok("Review approved.", projectId: project.Id, reviewId: review.Id);
    }

    public async Task<OrchestratorOperationResult> RejectAsync(Guid reviewId, string? comment, CancellationToken ct)
    {
        var review = await _db.Reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return OrchestratorOperationResult.Fail("Review not found.");
        if (review.Status != HumanReviewStatus.Pending)
            return OrchestratorOperationResult.Fail($"Review is already {review.Status}.");

        var project = review.Project ?? await _db.Projects.GetByIdAsync(review.ProjectId, ct)
            ?? throw new InvalidOperationException($"Project {review.ProjectId} missing.");

        await _db.ExecuteInTransactionAsync(async t =>
        {
            review.Status = HumanReviewStatus.Rejected;
            review.ResolvedAt = DateTime.UtcNow;
            review.ResolutionComment = comment;

            // A rejected review blocks the pipeline: pause it for human intervention.
            project.Status = ProjectStatus.Paused;
            project.UpdatedAt = DateTime.UtcNow;
            project.NextAction = "PAUSED_REJECTED_REVIEW";

            await AddEventCoreAsync(_db, project.Id, WorkflowEventType.HumanReviewRejected, review.Id,
                $"Review rejected: {review.Title}", comment, null, null, t);
            await AddEventCoreAsync(_db, project.Id, WorkflowEventType.ProjectPaused, null,
                "Project paused because a human review was rejected.", comment, null, null, t);

            await _db.SaveChangesAsync(t);
        }, ct);

        try { await _sync.SyncProjectAsync(project.Id, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Control center sync failed after review rejection."); }

        return OrchestratorOperationResult.Ok("Review rejected; project paused.", projectId: project.Id, reviewId: review.Id);
    }

    public async Task<OrchestratorOperationResult> CancelAsync(Guid reviewId, string? comment, CancellationToken ct)
    {
        var review = await _db.Reviews.GetByIdAsync(reviewId, ct);
        if (review is null) return OrchestratorOperationResult.Fail("Review not found.");
        if (review.Status != HumanReviewStatus.Pending)
            return OrchestratorOperationResult.Fail($"Review is already {review.Status}.");

        await _db.ExecuteInTransactionAsync(async t =>
        {
            review.Status = HumanReviewStatus.Cancelled;
            review.ResolvedAt = DateTime.UtcNow;
            review.ResolutionComment = comment;
            await _db.SaveChangesAsync(t);
        }, ct);

        await AddEventSafeAsync(review.ProjectId, WorkflowEventType.HumanReviewCancelled, reviewId, $"Review cancelled: {review.Title}", comment, ct);

        return OrchestratorOperationResult.Ok("Review cancelled.", projectId: review.ProjectId, reviewId: review.Id);
    }

    private async Task ApplyApprovalAsync(Domain.Entities.Project project, Domain.Entities.HumanReviewRequest review,
        ReviewPayload payload, string? resolutionPayloadJson, CancellationToken ct)
    {
        var asset = payload.AssetId is { } assetId ? await _db.Assets.GetByIdAsync(assetId, ct) : null;

        switch (review.ReviewType)
        {
            case HumanReviewType.ConceptSelection:
            {
                var conceptId = ExtractConceptId(resolutionPayloadJson);
                if (string.IsNullOrWhiteSpace(conceptId))
                    throw new InvalidOperationException("ConceptSelection requires a 'conceptId' in the resolution payload.");

                project.SelectedConceptJson = conceptId;
                await PublishAdvanceAsync(project, payload, asset, "Gate advanced after concept selection", ct);
                break;
            }

            case HumanReviewType.ImageGenerationApproval:
                await PublishAdvanceAsync(project, payload, asset, "Image generation approved; gate advanced.", ct);
                break;

            case HumanReviewType.PrePublication:
                await PublishAdvanceAsync(project, payload, asset, "Publication approved.", ct);
                project.Status = ProjectStatus.Completed;
                project.NextAction = "PUBLISHED";
                break;

            case HumanReviewType.QaUnresolvedIssues:
            {
                // Approval means "return to production to fix".
                var target = ParseGate(payload.TargetGate) ?? ProjectGate.BookProduction;
                if (!_transitions.CanRollback(project.CurrentGate, target))
                    throw new InvalidOperationException($"Cannot rollback from {project.CurrentGate} to {target}.");

                project.CurrentGate = target;
                project.UpdatedAt = DateTime.UtcNow;
                project.NextAction = $"RUN_{nameof(AgentCode.Production).ToUpperInvariant()}";
                await AddEventCoreAsync(_db, project.Id, WorkflowEventType.GateRolledBack, review.AgentRunId,
                    $"QA unresolved: rolled back to {target}.", null, project.CurrentGate, target, ct);
                break;
            }

            case HumanReviewType.AgentHumanReview:
            default:
                // Approving an agent review simply approves the produced work; no
                // gate action required. Deferred advance still applies if present.
                if (payload.TargetGate is not null)
                    await PublishAdvanceAsync(project, payload, asset, "Agent review approved.", ct);
                else if (asset is not null && asset.QaStatus != Domain.Enums.AssetQaStatus.Fail)
                    asset.Status = Domain.Enums.AssetStatus.Approved;
                break;
        }
    }

    private async Task PublishAdvanceAsync(Domain.Entities.Project project, ReviewPayload payload, Domain.Entities.Asset? asset, string description, CancellationToken ct)
    {
        var target = ParseGate(payload.TargetGate);
        if (target is null || !_transitions.CanTransition(project.CurrentGate, target.Value))
            throw new InvalidOperationException($"Advancing from {project.CurrentGate} to {target?.ToString() ?? "<unspecified>"} is not allowed.");

        var old = project.CurrentGate;
        project.CurrentGate = target.Value;
        project.UpdatedAt = DateTime.UtcNow;

        if (asset is not null && target.Value != ProjectGate.Published)
        {
            asset.Status = Domain.Enums.AssetStatus.Approved;
            await SupersedeSameTypeAsync(project.Id, asset, ct);
        }
        else if (asset is not null)
        {
            asset.Status = Domain.Enums.AssetStatus.Final;
        }

        project.NextAction = target.Value == ProjectGate.Published
            ? "PUBLISHED"
            : $"RUN_{AgentRouting.AgentForGate(target.Value).ToString().ToUpperInvariant()}";

        await AddEventCoreAsync(_db, project.Id, WorkflowEventType.GateAdvanced, payload.RunId,
            description, null, old, target.Value, ct);
    }

    private async Task SupersedeSameTypeAsync(Guid projectId, Domain.Entities.Asset promoted, CancellationToken ct)
    {
        foreach (var other in await _db.Assets.GetByProjectAsync(projectId, ct))
        {
            if (other.Id != promoted.Id && other.AssetType == promoted.AssetType && other.Status is Domain.Enums.AssetStatus.Approved)
                other.Status = Domain.Enums.AssetStatus.Superseded;
        }
    }

    private async Task MaybeContinueAsync(Domain.Entities.Project project, Domain.Entities.HumanReviewRequest resolvedReview, CancellationToken ct)
    {
        var pending = await _db.Reviews.GetPendingForProjectAsync(project.Id, ct);
        if (pending.Any(r => r.Id != resolvedReview.Id))
        {
            _logger.LogInformation("Project {ProjectCode} still has blocking reviews; no auto-continue.", project.ProjectCode);
            return;
        }

        if (project.Status != ProjectStatus.Active)
            return;

        await _jobs.EnqueueRunAgentAsync(project.Id, null, ct);
    }

    private static string? ExtractConceptId(string? resolutionPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(resolutionPayloadJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resolutionPayloadJson);
            return doc.RootElement.TryGetProperty("conceptId", out var conceptId) ? conceptId.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static ProjectGate? ParseGate(string? value) =>
        Enum.TryParse<ProjectGate>(value, true, out var gate) ? gate : null;

    private async Task AddEventSafeAsync(Guid projectId, WorkflowEventType type, Guid? reviewId, string? title, string? description, CancellationToken ct)
    {
        try
        {
            await _db.ExecuteInTransactionAsync(async t =>
            {
                await AddEventCoreAsync(_db, projectId, type, null, title, description, null, null, t);
                await _db.SaveChangesAsync(t);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record workflow event {Type}.", type);
        }
    }

    private static Task AddEventCoreAsync(IUnitOfWork db, Guid projectId, WorkflowEventType type, Guid? runId,
        string? title, string? description, ProjectGate? oldGate, ProjectGate? newGate, CancellationToken ct) =>
        db.Events.AddAsync(new Domain.Entities.WorkflowEvent
        {
            ProjectId = projectId,
            AgentRunId = runId,
            Type = type,
            OldGate = oldGate,
            NewGate = newGate,
            Title = title,
            Description = description,
            CreatedAt = DateTime.UtcNow,
        }, ct);
}