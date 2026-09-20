using System.Text.Json.Nodes;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.Rules;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>
/// Produces a recommendation (Level 2). Whatever this returns MUST be
/// re-validated by <see cref="IWorkflowTransitionValidator"/> before any
/// state change. It can recommend but never decide.
/// </summary>
public interface IOrchestratorDecider
{
    Task<OrchestratorDecision> DecideAsync(Project project, AgentExecutionResult result, CancellationToken ct);
}

/// <summary>
/// Deterministic Level-1-ish decision: maps structured specialist output to an
/// orchestrator recommendation using fixed rules (never a model call).
/// </summary>
public sealed class DeterministicOrchestratorDecider : IOrchestratorDecider
{
    private readonly IWorkflowTransitionValidator _transitions;

    public DeterministicOrchestratorDecider(IWorkflowTransitionValidator transitions)
    {
        _transitions = transitions;
    }

    public Task<OrchestratorDecision> DecideAsync(Project project, AgentExecutionResult result, CancellationToken ct)
    {
        var decision = Decide(project, result);
        return Task.FromResult(decision);
    }

    private OrchestratorDecision Decide(Project project, AgentExecutionResult result)
    {
        if (result.Status is not ("COMPLETE"))
        {
            return new OrchestratorDecision
            {
                Decision = nameof(OrchestratorDecisionKind.HumanReview),
                Reason = $"Agent returned status '{result.Status}'. Manual review required.",
                HumanReviewType = nameof(HumanReviewType.AgentHumanReview),
                BlockingIssues = [.. result.BlockingIssues],
            };
        }

        // Gate-specific mandatory rules.
        switch (project.CurrentGate)
        {
            case ProjectGate.MarketValidation when result.AgentCode == nameof(AgentCode.Validator):
            {
                var validator = Deserialize<ValidatorResult>(result.OutputJson);
                if (validator is not null && validator.Concepts.Count > 1)
                {
                    return new OrchestratorDecision
                    {
                        Decision = nameof(OrchestratorDecisionKind.HumanReview),
                        TargetGate = nameof(ProjectGate.Architecture),
                        HumanReviewType = nameof(HumanReviewType.ConceptSelection),
                        Reason = "Multiple validated concepts; a human must select the winning concept.",
                    };
                }
                break;
            }

            case ProjectGate.VisualProduction when result.AgentCode == nameof(AgentCode.ArtDirector):
            {
                var art = Deserialize<ArtDirectorResult>(result.OutputJson);
                if (art?.ImageGenerationPlan is { AssetsNeeded: > 0 } plan &&
                    plan.EstimatedTotalCost is > 0m)
                {
                    return new OrchestratorDecision
                    {
                        Decision = nameof(OrchestratorDecisionKind.HumanReview),
                        TargetGate = nameof(ProjectGate.BookProduction),
                        HumanReviewType = nameof(HumanReviewType.ImageGenerationApproval),
                        Reason = $"Mass image generation planned ({plan.AssetsNeeded} assets, est ${plan.EstimatedTotalCost:F2}).",
                    };
                }
                break;
            }

            case ProjectGate.Qa when result.AgentCode == nameof(AgentCode.Qa):
            {
                var qa = Deserialize<QaResult>(result.OutputJson);
                if (qa is { QaPassed: false })
                {
                    return new OrchestratorDecision
                    {
                        Decision = nameof(OrchestratorDecisionKind.HumanReview),
                        HumanReviewType = nameof(HumanReviewType.QaUnresolvedIssues),
                        Reason = "QA did not pass; human decision required before advancing.",
                        BlockingIssues = [.. qa.Issues.Select(i => $"[{i.Severity}] {i.Description}")],
                    };
                }
                break;
            }

            case ProjectGate.ReadyToPublish when result.AgentCode == nameof(AgentCode.Launch):
                return new OrchestratorDecision
                {
                    Decision = nameof(OrchestratorDecisionKind.HumanReview),
                    TargetGate = nameof(ProjectGate.Published),
                    HumanReviewType = nameof(HumanReviewType.PrePublication),
                    Reason = "Final publication requires explicit human approval.",
                };
        }

        return FromGateRecommendation(project, result);
    }

    private OrchestratorDecision FromGateRecommendation(Project project, AgentExecutionResult result)
    {
        var recommendation = result.GateRecommendation?.Trim();

        if (Is(recommendation, OrchestratorDecisionKind.Rollback))
            return new OrchestratorDecision
            {
                Decision = nameof(OrchestratorDecisionKind.Rollback),
                TargetGate = WorkflowStateMachine.PreviousGate(project.CurrentGate)?.ToString()
                             ?? throw new InvalidOperationException("Cannot rollback from this gate."),
                Reason = "Specialist requested a rollback.",
            };

        if (Is(recommendation, OrchestratorDecisionKind.Pause))
            return new OrchestratorDecision { Decision = nameof(OrchestratorDecisionKind.Pause), Reason = "Specialist requested a pause." };

        if (Is(recommendation, OrchestratorDecisionKind.Reject))
            return new OrchestratorDecision { Decision = nameof(OrchestratorDecisionKind.Reject), Reason = "Specialist recommended rejecting the project." };

        return new OrchestratorDecision
        {
            Decision = nameof(OrchestratorDecisionKind.AdvanceGate),
            TargetGate = WorkflowStateMachine.NextGate(project.CurrentGate)?.ToString(),
            Reason = "Specialist completed; advance to the next gate.",
        };
    }

    private static bool Is(string? candidate, OrchestratorDecisionKind kind) =>
        string.Equals(candidate, kind.ToString(), StringComparison.OrdinalIgnoreCase);

    private static TResult? Deserialize<TResult>(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<TResult>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
}