using System.Text.Json;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Tests;

public class DeterministicOrchestratorDeciderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeterministicOrchestratorDecider _decider = new(new WorkflowTransitionValidator());

    private static Project ProjectAt(ProjectGate gate) => new()
    {
        ProjectCode = "ZNV-001",
        CurrentGate = gate,
    };

    private static AgentExecutionResult Complete(AgentCode agent, string outputJson, string? recommendation = null) =>
        new()
        {
            AgentCode = agent.ToString(),
            ProjectCode = "ZNV-001",
            Status = "COMPLETE",
            OutputJson = outputJson,
            GateRecommendation = recommendation,
            Summary = "ok",
        };

    [Fact]
    public async Task Multiple_validated_concepts_request_human_selection()
    {
        var output = new ValidatorResult
        {
            Concepts =
            [
                new ValidatedConcept { ConceptId = "c1", Title = "A", MarketScore = 80 },
                new ValidatedConcept { ConceptId = "c2", Title = "B", MarketScore = 75 },
            ],
        };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.MarketValidation),
            Complete(AgentCode.Validator, JsonSerializer.Serialize(output, Json)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.HumanReview), decision.Decision);
        Assert.Equal(nameof(HumanReviewType.ConceptSelection), decision.HumanReviewType);
        Assert.Equal(nameof(ProjectGate.Architecture), decision.TargetGate);
    }

    [Fact]
    public async Task Single_concept_advances()
    {
        var output = new ValidatorResult
        {
            Concepts = [new ValidatedConcept { ConceptId = "c1", Title = "A", MarketScore = 80 }],
        };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.MarketValidation),
            Complete(AgentCode.Validator, JsonSerializer.Serialize(output, Json)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.AdvanceGate), decision.Decision);
        Assert.Equal(nameof(ProjectGate.Architecture), decision.TargetGate);
    }

    [Fact]
    public async Task Qa_fail_requires_human_decision()
    {
        var output = new QaResult
        {
            QaPassed = false,
            Issues = [new QaIssue { Severity = "high", Description = "Broken layout" }],
        };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.Qa),
            Complete(AgentCode.Qa, JsonSerializer.Serialize(output, Json)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.HumanReview), decision.Decision);
        Assert.Equal(nameof(HumanReviewType.QaUnresolvedIssues), decision.HumanReviewType);
        Assert.Contains("[high] Broken layout", decision.BlockingIssues);
    }

    [Fact]
    public async Task Qa_pass_advances_to_ready_to_publish()
    {
        var output = new QaResult { QaPassed = true };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.Qa),
            Complete(AgentCode.Qa, JsonSerializer.Serialize(output, Json)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.AdvanceGate), decision.Decision);
        Assert.Equal(nameof(ProjectGate.ReadyToPublish), decision.TargetGate);
    }

    [Fact]
    public async Task Launch_always_requires_pre_publication_approval()
    {
        var output = new LaunchResult { PublishDate = "2026-12-01" };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.ReadyToPublish),
            Complete(AgentCode.Launch, JsonSerializer.Serialize(output, Json)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.HumanReview), decision.Decision);
        Assert.Equal(nameof(HumanReviewType.PrePublication), decision.HumanReviewType);
        Assert.Equal(nameof(ProjectGate.Published), decision.TargetGate);
    }

    [Fact]
    public async Task Rollback_recommendation_targets_previous_gate()
    {
        var output = new WriterResult { Sections = [new ManuscriptSection { Title = "Ch1" }] };

        var decision = await _decider.DecideAsync(
            ProjectAt(ProjectGate.Manuscript),
            Complete(AgentCode.Writer, JsonSerializer.Serialize(output, Json), recommendation: nameof(OrchestratorDecisionKind.Rollback)),
            CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.Rollback), decision.Decision);
        Assert.Equal(nameof(ProjectGate.Architecture), decision.TargetGate);
    }

    [Fact]
    public async Task Pause_and_reject_recommendations_map()
    {
        var project = ProjectAt(ProjectGate.Manuscript);
        var writer = JsonSerializer.Serialize(new WriterResult { Sections = [new ManuscriptSection { Title = "Ch1" }] }, Json);

        var pause = await _decider.DecideAsync(project, Complete(AgentCode.Writer, writer, nameof(OrchestratorDecisionKind.Pause)), CancellationToken.None);
        var reject = await _decider.DecideAsync(project, Complete(AgentCode.Writer, writer, nameof(OrchestratorDecisionKind.Reject)), CancellationToken.None);

        Assert.Equal(nameof(OrchestratorDecisionKind.Pause), pause.Decision);
        Assert.Equal(nameof(OrchestratorDecisionKind.Reject), reject.Decision);
    }

    [Fact]
    public async Task Failed_agent_never_advances()
    {
        var decision = await _decider.DecideAsync(ProjectAt(ProjectGate.Manuscript), new AgentExecutionResult
        {
            AgentCode = nameof(AgentCode.Writer),
            ProjectCode = "ZNV-001",
            Status = "FAILED",
            OutputJson = "{}",
            BlockingIssues = ["model crashed"],
            Summary = string.Empty,
        }, CancellationToken.None);

        Assert.NotEqual(nameof(OrchestratorDecisionKind.AdvanceGate), decision.Decision);
        Assert.Equal(nameof(HumanReviewType.AgentHumanReview), decision.HumanReviewType);
        Assert.Contains("model crashed", decision.BlockingIssues);
    }
}

public class WorkflowTransitionValidatorTests
{
    private readonly IWorkflowTransitionValidator _validator = new WorkflowTransitionValidator();

    [Fact]
    public void Validates_forward_and_backward()
    {
        Assert.True(_validator.CanTransition(ProjectGate.Manuscript, ProjectGate.VisualProduction));
        Assert.False(_validator.CanTransition(ProjectGate.Manuscript, ProjectGate.BookProduction));
        Assert.True(_validator.CanRollback(ProjectGate.Manuscript, ProjectGate.Architecture));
        Assert.False(_validator.CanRollback(ProjectGate.Published, ProjectGate.Qa));
    }
}