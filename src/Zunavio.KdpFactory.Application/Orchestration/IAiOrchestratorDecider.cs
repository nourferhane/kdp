using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>
/// Level-2 AI reasoning on top of the deterministic decider. The model can
/// interpret specialist output and suggest a decision, but the suggestion is
/// validated against the C# rules before it is ever applied.
/// </summary>
public sealed class AiOrchestratorDecider : IOrchestratorDecider
{
    private static readonly string[] DecisionKinds =
    [
        nameof(OrchestratorDecisionKind.AdvanceGate),
        nameof(OrchestratorDecisionKind.Rollback),
        nameof(OrchestratorDecisionKind.Pause),
        nameof(OrchestratorDecisionKind.Reject),
        nameof(OrchestratorDecisionKind.HumanReview),
    ];

    private static readonly HashSet<string> MandatoryReviewTypes =
    [
        nameof(HumanReviewType.ConceptSelection),
        nameof(HumanReviewType.ImageGenerationApproval),
        nameof(HumanReviewType.QaUnresolvedIssues),
        nameof(HumanReviewType.PrePublication),
    ];

    private readonly IOrchestratorDecider _fallback;
    private readonly ILanguageModelClient _client;
    private readonly IAgentPromptLoader _promptLoader;
    private readonly IAgentDefinitionRepository _agents;
    private readonly IWorkflowTransitionValidator _transitions;
    private readonly IFactorySettingsProvider _settingsProvider;
    private readonly ILogger<AiOrchestratorDecider> _logger;

    public AiOrchestratorDecider(
        DeterministicOrchestratorDecider fallback,
        ILanguageModelClient client,
        IAgentPromptLoader promptLoader,
        IAgentDefinitionRepository agents,
        IWorkflowTransitionValidator transitions,
        IFactorySettingsProvider settingsProvider,
        ILogger<AiOrchestratorDecider> logger)
    {
        _fallback = fallback;
        _client = client;
        _promptLoader = promptLoader;
        _agents = agents;
        _transitions = transitions;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    public async Task<OrchestratorDecision> DecideAsync(Project project, AgentExecutionResult result, CancellationToken ct)
    {
        var settings = await _settingsProvider.GetAsync(ct);

        // Deterministic gate rules always win: mandatory human reviews are never
        // downgraded by the model.
        var fallback = await _fallback.DecideAsync(project, result, ct);
        if (fallback.HumanReviewType is not null && MandatoryReviewTypes.Contains(fallback.HumanReviewType))
            return fallback;

        if (!settings.EnableAiOrchestrator)
            return fallback;

        try
        {
            var orchestratorAgent = await _agents.GetByAgentCodeAsync(AgentCode.Orchestrator, ct);
            if (orchestratorAgent is null || !orchestratorAgent.Enabled)
            {
                _logger.LogInformation("AI orchestrator disabled: ORCHESTRATOR agent unavailable.");
                return fallback;
            }

            var snapshot = await _promptLoader.LoadAsync(orchestratorAgent, ct);
            var userContent = BuildDecisionPrompt(project, result);

            var response = await _client.CompleteStructuredAsync(new LanguageModelRequest
            {
                Model = settings.DefaultModel,
                Messages =
                [
                    LanguageModelMessage.System(snapshot.Text),
                    LanguageModelMessage.User(userContent),
                ],
                ResponseSchemaName = nameof(OrchestratorDecisionKind),
            }, ct);

            var parsed = ParseDecision(response.StructuredOutput.ToString());
            string? rejectionReason = null;
            if (parsed is null || !IsValidDecision(project, parsed, out rejectionReason))
            {
                _logger.LogWarning("AI orchestrator decision rejected ({Reason}); using deterministic fallback.", rejectionReason ?? "empty or unparsable");
                return fallback;
            }

            return parsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI orchestrator step failed; using deterministic fallback.");
            return fallback;
        }
    }

    private bool IsValidDecision(Project project, OrchestratorDecision decision, out string? reason)
    {
        reason = null;

        if (decision.Decision is null || !DecisionKinds.Contains(decision.Decision, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"Unknown decision '{decision.Decision}'.";
            return false;
        }

        if (decision.Decision.Equals(nameof(OrchestratorDecisionKind.AdvanceGate), StringComparison.OrdinalIgnoreCase))
        {
            var next = WorkflowStateMachine.NextGate(project.CurrentGate);
            // If the model named no target or named the correct next gate, accept.
            if (decision.TargetGate is not null)
            {
                var target = ParseGate(decision.TargetGate);
                if (target is not null && target != next)
                {
                    reason = $"ADVANCE_GATE to '{decision.TargetGate}' is not a legal transition.";
                    return false;
                }
            }
            decision.TargetGate = next?.ToString();
            return true;
        }

        if (decision.Decision.Equals(nameof(OrchestratorDecisionKind.Rollback), StringComparison.OrdinalIgnoreCase))
        {
            if (decision.TargetGate is null ||
                ParseGate(decision.TargetGate) is not { } target ||
                !_transitions.CanRollback(project.CurrentGate, target))
            {
                reason = $"ROLLBACK to '{decision.TargetGate}' is not allowed from '{project.CurrentGate}'.";
                return false;
            }
        }

        if (decision.HumanReviewType is not null &&
            !Enum.TryParse<HumanReviewType>(decision.HumanReviewType, true, out _))
        {
            reason = $"Unknown HumanReviewType '{decision.HumanReviewType}'.";
            return false;
        }

        return true;
    }

    private static OrchestratorDecision? ParseDecision(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node?.AsObject() is not { } root) return null;

            return new OrchestratorDecision
            {
                Decision = root["decision"]?.GetValue<string>(),
                TargetGate = root["targetGate"]?.GetValue<string>(),
                Reason = root["reason"]?.GetValue<string>(),
                NextAction = root["nextAction"]?.GetValue<string>(),
                HumanReviewType = root["humanReviewType"]?.GetValue<string>(),
                ReviewTitle = root["reviewTitle"]?.GetValue<string>(),
                ReviewDescription = root["reviewDescription"]?.GetValue<string>(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProjectGate? ParseGate(string value) =>
        Enum.TryParse<ProjectGate>(value, true, out var gate) ? gate : null;

    private static string BuildDecisionPrompt(Project project, AgentExecutionResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are the ORCHESTRATOR of the Zunavio KDP Factory.");
        sb.AppendLine($"Project: {project.ProjectCode} ({project.WorkingTitle}).");
        sb.AppendLine($"Current gate: {project.CurrentGate}. Next legal gate: {WorkflowStateMachine.NextGate(project.CurrentGate)?.ToString() ?? "-"}.");
        sb.AppendLine($"Specialist agent: {result.AgentCode}. Status: {result.Status}.");
        sb.AppendLine($"Specialist summary: {result.Summary}");
        sb.AppendLine($"Blocking issues: {(result.BlockingIssues.Count == 0 ? "none" : string.Join("; ", result.BlockingIssues))}");
        sb.AppendLine("Specialist structured result (JSON):");
        sb.AppendLine(result.OutputJson);
        sb.AppendLine();
        sb.AppendLine("Return a single JSON object with the keys:");
        sb.AppendLine("decision (one of ADVANCE_GATE, ROLLBACK, PAUSE, REJECT, HUMAN_REVIEW),");
        sb.AppendLine("targetGate (gate name or null), reason, nextAction, humanReviewType, reviewTitle, reviewDescription.");
        sb.AppendLine("Do not invent gates. ADVANCE_GATE must use the next legal gate.");
        return sb.ToString();
    }
}