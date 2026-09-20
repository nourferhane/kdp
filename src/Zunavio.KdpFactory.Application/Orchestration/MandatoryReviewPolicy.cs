using System.Text.Json;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>Description of a blocking review to create before a gate advances.</summary>
public sealed record BlockingReviewSpec
{
    public required HumanReviewType Type { get; init; }
    public ProjectGate? TargetGate { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required ReviewPayload Payload { get; init; }
}

/// <summary>
/// Evaluates the mandatory human review situations defined in section 4.
/// Returns a spec when the gate advance must be blocked; null otherwise.
/// </summary>
public interface IMandatoryReviewPolicy
{
    Task<BlockingReviewSpec?> EvaluateAsync(Project project, AgentExecutionResult result, CancellationToken ct);
}

public sealed class MandatoryReviewPolicy : IMandatoryReviewPolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Task<BlockingReviewSpec?> EvaluateAsync(Project project, AgentExecutionResult result, CancellationToken ct)
    {
        var agentCode = (AgentCode)Enum.Parse(typeof(AgentCode), result.AgentCode, ignoreCase: true);

        BlockingReviewSpec? spec = (project.CurrentGate, agentCode, result.Status) switch
        {
            (ProjectGate.MarketValidation, AgentCode.Validator, "COMPLETE") => EvaluateConceptSelection(result),
            (ProjectGate.VisualProduction, AgentCode.ArtDirector, "COMPLETE") => EvaluateImageGeneration(result),
            (ProjectGate.Qa, AgentCode.Qa, "COMPLETE") => EvaluateQa(result),
            (ProjectGate.ReadyToPublish, AgentCode.Launch, "COMPLETE") => new BlockingReviewSpec
            {
                Type = HumanReviewType.PrePublication,
                TargetGate = ProjectGate.Published,
                Title = "Approve publication",
                Description = "Final approval is required before the book is published.",
                Payload = new ReviewPayload { TargetGate = nameof(ProjectGate.Published) },
            },
            _ => null,
        };

        return Task.FromResult(spec);
    }

    private static BlockingReviewSpec? EvaluateConceptSelection(AgentExecutionResult result)
    {
        var validator = JsonSerializer.Deserialize<ValidatorResult>(result.OutputJson, Options);
        if (validator is null || validator.Concepts.Count <= 1)
            return null;

        return new BlockingReviewSpec
        {
            Type = HumanReviewType.ConceptSelection,
            TargetGate = ProjectGate.Architecture,
            Title = "Select the winning concept",
            Description = "Several concepts passed validation. Pick the one the architect should develop.",
            Payload = new ReviewPayload
            {
                ReviewType = nameof(HumanReviewType.ConceptSelection),
                TargetGate = nameof(ProjectGate.Architecture),
                Concepts = validator.Concepts
                    .Select(c => new ConceptOption
                    {
                        ConceptId = c.ConceptId,
                        Title = c.Title,
                        MarketScore = c.MarketScore,
                        Positioning = c.Positioning,
                    })
                    .ToList(),
            },
        };
    }

    private static BlockingReviewSpec? EvaluateImageGeneration(AgentExecutionResult result)
    {
        var art = JsonSerializer.Deserialize<ArtDirectorResult>(result.OutputJson, Options);
        var plan = art?.ImageGenerationPlan;
        if (plan is null || plan.AssetsNeeded <= 0)
            return null;

        return new BlockingReviewSpec
        {
            Type = HumanReviewType.ImageGenerationApproval,
            TargetGate = ProjectGate.BookProduction,
            Title = "Approve mass image generation",
            Description = $"{plan.AssetsNeeded} visual assets are planned (estimated ${plan.EstimatedTotalCost:F2}). Approval is required before this cost is committed.",
            Payload = new ReviewPayload
            {
                ReviewType = nameof(HumanReviewType.ImageGenerationApproval),
                TargetGate = nameof(ProjectGate.BookProduction),
            },
        };
    }

    private static BlockingReviewSpec? EvaluateQa(AgentExecutionResult result)
    {
        var qa = JsonSerializer.Deserialize<QaResult>(result.OutputJson, Options);
        if (qa is not { QaPassed: false })
            return null;

        return new BlockingReviewSpec
        {
            Type = HumanReviewType.QaUnresolvedIssues,
            TargetGate = ProjectGate.BookProduction,
            Title = "QA found important unresolved issues",
            Description = string.Join(Environment.NewLine, qa.Issues.Select(i => $"- [{i.Severity}] {i.Category}: {i.Description}")),
            Payload = new ReviewPayload
            {
                ReviewType = nameof(HumanReviewType.QaUnresolvedIssues),
                TargetGate = nameof(ProjectGate.BookProduction),
            },
        };
    }
}