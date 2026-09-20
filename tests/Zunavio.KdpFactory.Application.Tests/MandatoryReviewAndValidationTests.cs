using System.Text.Json;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Tests;

public class MandatoryReviewPolicyTests
{
    private readonly IMandatoryReviewPolicy _policy = new MandatoryReviewPolicy();
    private static readonly Project Project = new() { ProjectCode = "ZNV-001" };

    private static AgentExecutionResult Complete(AgentCode agent, string outputJson) => new()
    {
        AgentCode = agent.ToString(),
        ProjectCode = "ZNV-001",
        Status = "COMPLETE",
        OutputJson = outputJson,
        Summary = "ok",
    };

    [Fact]
    public async Task Concept_selection_required_for_multiple_concepts()
    {
        var project = ProjectAt(ProjectGate.MarketValidation);
        var output = new ValidatorResult
        {
            Concepts =
            [
                new ValidatedConcept { ConceptId = "c1", Title = "A", MarketScore = 80 },
                new ValidatedConcept { ConceptId = "c2", Title = "B", MarketScore = 75 },
            ],
        };

        var spec = await _policy.EvaluateAsync(project, Complete(AgentCode.Validator, Json(output)), CancellationToken.None);

        Assert.NotNull(spec);
        Assert.Equal(HumanReviewType.ConceptSelection, spec.Type);
        Assert.Equal(ProjectGate.Architecture, spec.TargetGate);
    }

    [Fact]
    public async Task Image_generation_approval_required_when_cost_planned()
    {
        var project = ProjectAt(ProjectGate.VisualProduction);
        var output = new ArtDirectorResult
        {
            StyleGuide = "yes",
            ImageGenerationPlan = new ImageGenerationPlan { AssetsNeeded = 12, EstimatedTotalCost = 149.50m },
        };

        var spec = await _policy.EvaluateAsync(project, Complete(AgentCode.ArtDirector, Json(output)), CancellationToken.None);

        Assert.NotNull(spec);
        Assert.Equal(HumanReviewType.ImageGenerationApproval, spec.Type);
    }

    [Fact]
    public async Task Qa_failure_always_blocking()
    {
        var project = ProjectAt(ProjectGate.Qa);
        var output = new QaResult { QaPassed = false, Issues = [new QaIssue { Severity = "high", Description = "x" }] };

        var spec = await _policy.EvaluateAsync(project, Complete(AgentCode.Qa, Json(output)), CancellationToken.None);

        Assert.NotNull(spec);
        Assert.Equal(HumanReviewType.QaUnresolvedIssues, spec.Type);
    }

    [Fact]
    public async Task Qa_pass_is_not_blocking()
    {
        var project = ProjectAt(ProjectGate.Qa);
        var output = new QaResult { QaPassed = true, Issues = [] };

        var spec = await _policy.EvaluateAsync(project, Complete(AgentCode.Qa, Json(output)), CancellationToken.None);

        Assert.Null(spec);
    }

    [Fact]
    public async Task Launch_gets_pre_publication_review()
    {
        var project = ProjectAt(ProjectGate.ReadyToPublish);
        var output = new LaunchResult { PublishDate = "2026-12-01" };

        var spec = await _policy.EvaluateAsync(project, Complete(AgentCode.Launch, Json(output)), CancellationToken.None);

        Assert.NotNull(spec);
        Assert.Equal(HumanReviewType.PrePublication, spec.Type);
        Assert.Equal(ProjectGate.Published, spec.TargetGate);
    }

    private static Project ProjectAt(ProjectGate gate) => new Project { ProjectCode = "ZNV-001", CurrentGate = gate };

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions(JsonSerializerDefaults.Web));
}

public class AgentOutputValidatorTests
{
    private readonly IAgentOutputValidator _validator = new AgentOutputValidator();
    private const string Base = """{"agentCode":"Writer","projectCode":"ZNV-001","status":"COMPLETE","blockingIssues":[],"summary":"done","sections":[{"title":"Ch1"}]}""";

    [Fact]
    public void Accepts_valid_complete_output()
    {
        var result = _validator.Validate(AgentCode.Writer, Base);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("""{"projectCode":"ZNV-001","status":"COMPLETE","summary":"done","sections":[]}""")]
    [InlineData("""{"agentCode":"Writer","projectCode":"ZNV-001","status":"SOMETHING","summary":"done","sections":[]}""")]
    [InlineData("""{"agentCode":"Validator","projectCode":"ZNV-001","status":"COMPLETE","summary":"done","sections":[]}""")]
    public void Rejects_malformed_output(string output)
    {
        var result = _validator.Validate(AgentCode.Writer, output);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_empty_sections_for_complete_writer()
    {
        var output = """{"agentCode":"Writer","projectCode":"ZNV-001","status":"COMPLETE","summary":"done","sections":[]}""";
        var result = _validator.Validate(AgentCode.Writer, output);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("sections", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_non_json()
    {
        var result = _validator.Validate(AgentCode.Writer, "not json {");
        Assert.False(result.IsValid);
    }
}