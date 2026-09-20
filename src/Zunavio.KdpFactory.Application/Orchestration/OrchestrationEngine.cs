using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;
using Zunavio.KdpFactory.Application.Services;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>
/// The internal execution engine used by the background worker. It never runs
/// inside an HTTP request and it is the only place where gate changes happen.
/// The flow is strictly: prompt snapshot → input assembly → model call →
/// deserialize → validate → domain rules → persist (section 5).
/// </summary>
public interface IOrchestrationEngine
{
    Task ExecuteRunAgentAsync(RunAgentJobPayload payload, CancellationToken ct);
    Task ExecuteImportControlCenterAsync(ImportControlCenterJobPayload payload, CancellationToken ct);
    Task ExecuteSyncProjectAsync(SyncProjectJobPayload payload, CancellationToken ct);
    Task ExecuteRefreshAgentPromptsAsync(RefreshAgentPromptsJobPayload payload, CancellationToken ct);
}

public sealed class OrchestrationEngine : IOrchestrationEngine
{
    private readonly IUnitOfWork _db;
    private readonly IAgentPromptLoader _prompts;
    private readonly IAgentContextBuilder _contexts;
    private readonly IAgentExecutor _executor;
    private readonly IAgentOutputValidator _outputValidator;
    private readonly IOrchestratorDecider _decider;
    private readonly IWorkflowTransitionValidator _transitions;
    private readonly IMandatoryReviewPolicy _reviews;
    private readonly IVersionService _versions;
    private readonly ICodeGenerator _codes;
    private readonly IArtifactStorage _artifacts;
    private readonly IFactorySettingsProvider _settings;
    private readonly IGoogleControlCenterSyncService _sync;
    private readonly ILogger<OrchestrationEngine> _logger;

    public OrchestrationEngine(
        IUnitOfWork db,
        IAgentPromptLoader prompts,
        IAgentContextBuilder contexts,
        IAgentExecutor executor,
        IAgentOutputValidator outputValidator,
        IOrchestratorDecider decider,
        IWorkflowTransitionValidator transitions,
        IMandatoryReviewPolicy reviews,
        IVersionService versions,
        ICodeGenerator codes,
        IArtifactStorage artifacts,
        IFactorySettingsProvider settings,
        IGoogleControlCenterSyncService sync,
        ILogger<OrchestrationEngine> logger)
    {
        _db = db;
        _prompts = prompts;
        _contexts = contexts;
        _executor = executor;
        _outputValidator = outputValidator;
        _decider = decider;
        _transitions = transitions;
        _reviews = reviews;
        _versions = versions;
        _codes = codes;
        _artifacts = artifacts;
        _settings = settings;
        _sync = sync;
        _logger = logger;
    }

    public async Task ExecuteRunAgentAsync(RunAgentJobPayload payload, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(payload.ProjectId, ct)
            ?? throw new InvalidOperationException($"Project '{payload.ProjectId}' not found.");

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ProjectCode"] = project.ProjectCode,
        });

        if (project.Status != ProjectStatus.Active)
        {
            _logger.LogInformation("Project {ProjectCode} is not ACTIVE; skipping run.", project.ProjectCode);
            return;
        }

        if (await HasBlockingReviewAsync(project.Id, ct))
        {
            _logger.LogInformation("Project {ProjectCode} has a pending review; skipping run.", project.ProjectCode);
            return;
        }

        if (project.CurrentGate is ProjectGate.Published or ProjectGate.None)
        {
            _logger.LogInformation("Project {ProjectCode} is at terminal gate {Gate}; no agent to run.", project.ProjectCode, project.CurrentGate);
            return;
        }

        var wiredAgent = AgentRouting.AgentForGate(project.CurrentGate);

        if (payload.AgentCode is not null)
        {
            var requested = (AgentCode)Enum.Parse(typeof(AgentCode), payload.AgentCode, ignoreCase: true);
            if (requested != wiredAgent)
                throw new InvalidOperationException(
                    $"Agent '{requested}' does not own gate '{project.CurrentGate}'. " +
                    $"Expected '{wiredAgent}'. Agents may not skip gates.");
        }

        var agent = await _db.Agents.GetByAgentCodeAsync(wiredAgent, ct)
            ?? throw new InvalidOperationException($"Agent '{wiredAgent}' is not registered.");

        if (!agent.Enabled)
            throw new InvalidOperationException($"Agent '{agent.Code}' is disabled.");

        var idempotencyKey = $"{project.Id}:{project.CurrentGate}:{agent.Code}";
        if (await _db.Runs.HasActiveDuplicateAsync(idempotencyKey, ct))
        {
            _logger.LogInformation("Duplicate active run for {ProjectCode} at {Gate}; skipping.", project.ProjectCode, project.CurrentGate);
            return;
        }

        AgentRun run;

        // Reuse a previously failed run for the same logical pipeline step so that
        // job retries do not mint duplicate AgentRun rows.
        var previous = await _db.Runs.GetLatestForKeyAsync(idempotencyKey, ct);
        if (previous is not null && previous.Status == AgentRunStatus.Failed)
        {
            _logger.LogInformation("Reusing failed run {RunCode} for {ProjectCode} at {Gate}; retry.", previous.RunCode, project.ProjectCode, project.CurrentGate);
            run = previous;
            run.Status = AgentRunStatus.Running;
            run.RetryCount += 1;
            run.ErrorMessage = null;
            run.StartedAt = DateTime.UtcNow;
            run.CompletedAt = null;
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            run = new AgentRun
            {
                ProjectId = project.Id,
                AgentDefinitionId = agent.Id,
                RunCode = _codes.RunCode(project.ProjectCode, await _db.Runs.GetRunSequenceAsync(project.Id, ct) + 1),
                Status = AgentRunStatus.Running,
                StartedAt = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTime.UtcNow,
            };

            await _db.ExecuteInTransactionAsync(async t =>
            {
                await _db.Runs.AddAsync(run, t);
                await _db.Events.AddAsync(new WorkflowEvent
                {
                    ProjectId = project.Id,
                    AgentRunId = run.Id,
                    Type = WorkflowEventType.AgentStarted,
                    Title = $"Agent {agent.Code} started.",
                    CreatedAt = DateTime.UtcNow,
                }, t);
                await _db.SaveChangesAsync(t);
            }, ct);
        }

        using var runScope = _logger.BeginScope(new Dictionary<string, object> { ["RunCode"] = run.RunCode, ["AgentCode"] = agent.Code });

        try
        {
            // 1. Retrieve the current prompt (Drive) and record an immutable snapshot.
            var prompt = await _prompts.LoadAsync(agent, ct);

            // 2. Assemble only the context this agent needs.
            var context = await _contexts.BuildAsync(project, agent, prompt, idempotencyKey, ct);
            run.InputVersion = context.InputVersion;
            run.PromptSnapshot = prompt.Text;
            run.PromptVersion = prompt.Version;
            run.PromptHash = prompt.Hash;
            run.Model = context.Model;

            // 3. Execute the agent (AI execution boundary).
            var result = await _executor.ExecuteAsync(context, ct);

            run.Model = result.Metadata.Model ?? run.Model;
            run.OpenAiRequestId = result.Metadata.OpenAiRequestId;
            run.InputTokens = result.Metadata.InputTokens;
            run.OutputTokens = result.Metadata.OutputTokens;
            run.EstimatedCost = result.Metadata.EstimatedCost;

            // 4. Deserialize → validate.
            var validation = _outputValidator.Validate(agent.ToAgentCode(), result.OutputJson);
            if (!validation.IsValid)
                throw new InvalidOperationException($"Agent output validation failed: {string.Join("; ", validation.Errors)}");

            run.OutputJson = result.OutputJson;
            run.Summary = Truncate(result.Summary, 4000);
            run.BlockingIssuesJson = System.Text.Json.JsonSerializer.Serialize(result.BlockingIssues);
            run.GateRecommendation = result.GateRecommendation;

            var asset = await PersistProducedAssetAsync(project, agent.ToAgentCode(), run, result, ct);

            // 5. Domain rules: decide and apply — always validated.
            var decision = await _decider.DecideAsync(project, result, ct);
            await ApplyDecisionAsync(project, agent.ToAgentCode(), run, result, asset, decision, ct);

            run.Status = AgentRunStatus.Complete;
            run.CompletedAt = DateTime.UtcNow;
            run.OutputVersion = asset?.Version ?? run.OutputVersion;
            project.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Agent {AgentCode} completed for {ProjectCode} at {Gate}; next: {NextAction}.",
                agent.Code, project.ProjectCode, project.CurrentGate, project.NextAction);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Status = AgentRunStatus.Cancelled;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = "Run cancelled.";
            await _db.SaveChangesAsync(ct);
            throw;
        }
        catch (Exception ex)
        {
            await HandleAgentFailureAsync(project, agent.ToAgentCode(), run, ex, ct);
            throw;
        }

        await SyncBestEffortAsync(project.Id, ct);
    }

    public Task ExecuteImportControlCenterAsync(ImportControlCenterJobPayload payload, CancellationToken ct) =>
        _sync.ImportAsync(ct);

    public async Task ExecuteSyncProjectAsync(SyncProjectJobPayload payload, CancellationToken ct)
    {
        await _sync.SyncProjectAsync(payload.ProjectId, ct);
    }

    public async Task ExecuteRefreshAgentPromptsAsync(RefreshAgentPromptsJobPayload payload, CancellationToken ct)
    {
        var agents = await _db.Agents.GetAllAsync(ct);
        foreach (var agent in agents)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["AgentCode"] = agent.Code });
            try
            {
                await _prompts.RefreshAsync(agent, ct);
                _logger.LogInformation("Refreshed prompt for agent {AgentCode} (v{Version}).", agent.Code, agent.PromptVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh prompt for agent {AgentCode}; keeping cached copy.", agent.Code);
            }
        }
    }

    private async Task<Asset?> PersistProducedAssetAsync(Project project, AgentCode agentCode, AgentRun run, AgentExecutionResult result, CancellationToken ct)
    {
        var assetType = AssetTypeFor(agentCode);
        var latest = await _db.Assets.GetLatestAsync(project.Id, assetType, ct);

        // Drafts of the same cycle get draft numbers; re-runs replace the previous draft.
        var version = latest is null
            ? _versions.NextDraft(null)
            : latest.Status is AssetStatus.Approved
                ? _versions.NextApproved(latest.Version)
                : _versions.NextDraft(latest.Version);

        var versionText = version.ToString();

        string? driveFileId = null;
        string? driveUrl = null;

        var settings = await _settings.GetAsync(ct);
        if (settings.GoogleEnabled)
        {
            try
            {
                var title = $"{project.ProjectCode}_{Enum.GetName(assetType)!.ToUpperInvariant()}_v{versionText}";
                var saved = await _artifacts.SaveArtifactAsync(project.ProjectCode, assetType, versionText, title, result.OutputJson, ct);
                driveFileId = saved.DriveFileId;
                driveUrl = saved.DriveUrl;
                _logger.LogInformation("Saved {AssetType} for {ProjectCode} to Drive ({DriveFileId}).", assetType, project.ProjectCode, saved.DriveFileId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Drive save failed for {AssetType} of {ProjectCode}; content stays in PostgreSQL.", assetType, project.ProjectCode);
            }
        }

        var assetCode = _codes.AssetCode(project.ProjectCode, assetType, $"v{versionText}");
        var asset = new Asset
        {
            AssetCode = assetCode,
            ProjectId = project.Id,
            AssetType = assetType,
            Version = versionText,
            DriveFileId = driveFileId,
            DriveUrl = driveUrl,
            Status = AssetStatus.Draft,
            CreatedByAgentRunId = run.Id,
            ContentJson = result.OutputJson,
            QaStatus = agentCode == AgentCode.Qa
                ? System.Text.Json.JsonSerializer.Deserialize<Agents.QaResult>(result.OutputJson) is { } qa && qa.QaPassed
                    ? AssetQaStatus.Pass
                    : AssetQaStatus.Fail
                : AssetQaStatus.NotChecked,
            CreatedAt = DateTime.UtcNow,
        };

        await _db.Assets.AddAsync(asset, ct);
        ApplyCurrentVersionField(project, assetType, versionText);

        await _db.Events.AddAsync(new WorkflowEvent
        {
            ProjectId = project.Id,
            AgentRunId = run.Id,
            Type = WorkflowEventType.AssetCreated,
            Title = $"Asset {asset.AssetCode} created (v{versionText}).",
            CreatedAt = DateTime.UtcNow,
        }, ct);

        run.OutputVersion = versionText;
        return asset;
    }

    private async Task ApplyDecisionAsync(Project project, AgentCode agentCode, AgentRun run,
        AgentExecutionResult result, Asset? asset, OrchestratorDecision decision, CancellationToken ct)
    {
        var kind = ParseDecisionKind(decision.Decision);

        switch (kind)
        {
            case OrchestratorDecisionKind.AdvanceGate:
            {
                var target = WorkflowStateMachine.NextGate(project.CurrentGate);
                if (target is null)
                {
                    _logger.LogWarning("Cannot advance from {Gate}: no next gate.", project.CurrentGate);
                    break;
                }

                var reviewSpec = await _reviews.EvaluateAsync(project, result, ct);
                if (reviewSpec is not null)
                {
                    // Human gate first: the advance is deferred until approval.
                    reviewSpec.Payload.TargetGate ??= target.ToString();
                    if (asset is not null) reviewSpec.Payload.AssetId = asset.Id;
                    reviewSpec.Payload.RunId = run.Id;
                    await CreateReviewAsync(project, run, reviewSpec, ct);
                    break;
                }

                project.CurrentGate = target.Value;
                project.UpdatedAt = DateTime.UtcNow;
                project.MarketScore ??= await ExtractMarketScoreAsync(result.OutputJson, ct);
                project.NextAction = $"RUN_{AgentRouting.AgentForGate(target.Value).ToString().ToUpperInvariant()}";
                if (asset is not null)
                {
                    asset.Status = AssetStatus.Approved;
                    await SupersedeSameTypeAsync(project.Id, asset, ct);
                }

                await _db.Events.AddAsync(new WorkflowEvent
                {
                    ProjectId = project.Id,
                    AgentRunId = run.Id,
                    Type = WorkflowEventType.GateAdvanced,
                    OldGate = WorkflowStateMachine.PreviousGate(target.Value),
                    NewGate = target.Value,
                    Title = $"Gate advanced to {target.Value}.",
                    CreatedAt = DateTime.UtcNow,
                }, ct);
                break;
            }

            case OrchestratorDecisionKind.Rollback:
            {
                var target = ParseGate(decision.TargetGate) ?? WorkflowStateMachine.PreviousGate(project.CurrentGate);
                if (target is null || !_transitions.CanRollback(project.CurrentGate, target.Value))
                    throw new InvalidOperationException($"Rollback from {project.CurrentGate} to {target?.ToString() ?? "<none>"} is not allowed.");

                var old = project.CurrentGate;
                project.CurrentGate = target.Value;
                project.UpdatedAt = DateTime.UtcNow;
                project.NextAction = $"RUN_{AgentRouting.AgentForGate(target.Value).ToString().ToUpperInvariant()}";

                await _db.Events.AddAsync(new WorkflowEvent
                {
                    ProjectId = project.Id,
                    AgentRunId = run.Id,
                    Type = WorkflowEventType.GateRolledBack,
                    OldGate = old,
                    NewGate = target.Value,
                    Title = $"Gate rolled back to {target.Value}.",
                    CreatedAt = DateTime.UtcNow,
                }, ct);
                break;
            }

            case OrchestratorDecisionKind.Pause:
                project.Status = ProjectStatus.Paused;
                project.UpdatedAt = DateTime.UtcNow;
                project.NextAction = "PAUSED";
                await AddEventAsync(project.Id, run.Id, WorkflowEventType.ProjectPaused, $"Paused after {agentCode} run.", ct);
                break;

            case OrchestratorDecisionKind.Reject:
                project.Status = ProjectStatus.Rejected;
                project.UpdatedAt = DateTime.UtcNow;
                project.NextAction = "REJECTED";
                await AddEventAsync(project.Id, run.Id, WorkflowEventType.ProjectRejected, $"Project rejected after {agentCode} run.", ct);
                break;

            case OrchestratorDecisionKind.HumanReview:
            case OrchestratorDecisionKind.None:
            default:
                await CreateReviewAsync(project, run, new BlockingReviewSpec
                {
                    Type = ParseReviewType(decision.HumanReviewType) ?? HumanReviewType.AgentHumanReview,
                    Title = decision.ReviewTitle ?? $"{agentCode} requires human review",
                    Description = decision.ReviewDescription ?? decision.Reason,
                    Payload = new ReviewPayload
                    {
                        ReviewType = decision.HumanReviewType,
                        TargetGate = decision.TargetGate,
                        RunId = run.Id,
                    },
                }, ct);
                break;
        }
    }

    private async Task CreateReviewAsync(Project project, AgentRun run, BlockingReviewSpec spec, CancellationToken ct)
    {
        await _db.Reviews.AddAsync(new HumanReviewRequest
        {
            ProjectId = project.Id,
            AgentRunId = run.Id,
            ReviewType = spec.Type,
            Title = spec.Title,
            Description = spec.Description,
            PayloadJson = spec.Payload.ToJson(),
            Status = HumanReviewStatus.Pending,
            RequestedAt = DateTime.UtcNow,
        }, ct);

        project.UpdatedAt = DateTime.UtcNow;
        project.NextAction = "WAIT_HUMAN_REVIEW";

        await _db.Events.AddAsync(new WorkflowEvent
        {
            ProjectId = project.Id,
            AgentRunId = run.Id,
            Type = WorkflowEventType.HumanReviewRequested,
            Title = $"Human review requested: {spec.Title}",
            Description = spec.Description,
            CreatedAt = DateTime.UtcNow,
        }, ct);

        _logger.LogInformation("Created human review '{Title}' for {ProjectCode} (blocks gate advance).", spec.Title, project.ProjectCode);
    }

    private async Task HandleAgentFailureAsync(Project project, AgentCode agentCode, AgentRun run, Exception ex, CancellationToken ct)
    {
        run.Status = AgentRunStatus.Failed;
        run.CompletedAt = DateTime.UtcNow;
        run.ErrorMessage = Truncate(ex.Message, 4000);
        run.RetryCount += 1;
        project.UpdatedAt = DateTime.UtcNow;
        project.NextAction = $"RETRY_{agentCode.ToString().ToUpperInvariant()}";

        await _db.Events.AddAsync(new WorkflowEvent
        {
            ProjectId = project.Id,
            AgentRunId = run.Id,
            Type = WorkflowEventType.AgentFailed,
            Title = $"Agent {agentCode} failed.",
            Description = Truncate(ex.Message, 2000),
            CreatedAt = DateTime.UtcNow,
        }, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogError(ex, "Agent {AgentCode} run {RunCode} failed for {ProjectCode}. Gate NOT advanced.", agentCode, run.RunCode, project.ProjectCode);
    }

    private async Task<bool> HasBlockingReviewAsync(Guid projectId, CancellationToken ct) =>
        (await _db.Reviews.GetPendingForProjectAsync(projectId, ct)).Count > 0;

    private async Task SupersedeSameTypeAsync(Guid projectId, Asset promoted, CancellationToken ct)
    {
        foreach (var other in await _db.Assets.GetByProjectAsync(projectId, ct))
        {
            if (other.Id != promoted.Id && other.AssetType == promoted.AssetType && other.Status is AssetStatus.Approved)
                other.Status = AssetStatus.Superseded;
        }
    }

    private async Task SyncBestEffortAsync(Guid projectId, CancellationToken ct)
    {
        try
        {
            var settings = await _settings.GetAsync(ct);
            if (settings.GoogleEnabled)
                await _sync.SyncProjectAsync(projectId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Control Center sync failed for project {ProjectId}; PostgreSQL remains the source of truth.", projectId);
        }
    }

    private static AssetType AssetTypeFor(AgentCode agentCode) => agentCode switch
    {
        AgentCode.Scout => AssetType.ScoutReport,
        AgentCode.Validator => AssetType.ValidatorReport,
        AgentCode.Architect => AssetType.ProductArchitecture,
        AgentCode.Writer => AssetType.Manuscript,
        AgentCode.ArtDirector => AssetType.VisualBible,
        AgentCode.Production => AssetType.InteriorPdf,
        AgentCode.Metadata => AssetType.Metadata,
        AgentCode.Qa => AssetType.QaReport,
        AgentCode.Launch => AssetType.Epub,
        _ => throw new ArgumentOutOfRangeException(nameof(agentCode), agentCode, null),
    };

    private static void ApplyCurrentVersionField(Project project, AssetType assetType, string version)
    {
        switch (assetType)
        {
            case AssetType.Manuscript:
                project.CurrentManuscriptVersion = version;
                break;
            case AssetType.VisualBible:
                project.CurrentVisualBibleVersion = version;
                break;
            case AssetType.InteriorPdf:
            case AssetType.Cover:
            case AssetType.Epub:
                project.CurrentProductionVersion = version;
                break;
        }
    }

    private static async Task<int?> ExtractMarketScoreAsync(string outputJson, CancellationToken ct)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(outputJson);
            if (doc.RootElement.TryGetProperty("marketScore", out var score) && score.ValueKind == System.Text.Json.JsonValueKind.Number)
                return score.GetInt32();
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static OrchestratorDecisionKind ParseDecisionKind(string? value) =>
        Enum.TryParse<OrchestratorDecisionKind>(value, ignoreCase: true, out var kind) && kind != OrchestratorDecisionKind.None
            ? kind
            : OrchestratorDecisionKind.AdvanceGate;

    private static ProjectGate? ParseGate(string? value) =>
        Enum.TryParse<ProjectGate>(value, ignoreCase: true, out var gate) ? gate : null;

    private static HumanReviewType? ParseReviewType(string? value) =>
        Enum.TryParse<HumanReviewType>(value, ignoreCase: true, out var type) ? type : null;

    private Task AddEventAsync(Guid projectId, Guid? runId, WorkflowEventType type, string title, CancellationToken ct) =>
        _db.Events.AddAsync(new WorkflowEvent
        {
            ProjectId = projectId,
            AgentRunId = runId,
            Type = type,
            Title = title,
            CreatedAt = DateTime.UtcNow,
        }, ct);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}