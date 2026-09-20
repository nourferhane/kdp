using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

/// <summary>
/// The Orchestrator's structured decision. Produced by the optional AI
/// orchestrator step and/or the deterministic rules, then validated by the
/// C# transition validator before it can touch the database.
/// </summary>
public sealed class OrchestratorDecision
{
    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("targetGate")]
    public string? TargetGate { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("nextAction")]
    public string? NextAction { get; set; }

    [JsonPropertyName("humanReviewType")]
    public string? HumanReviewType { get; set; }

    [JsonPropertyName("reviewTitle")]
    public string? ReviewTitle { get; set; }

    [JsonPropertyName("reviewDescription")]
    public string? ReviewDescription { get; set; }

    [JsonPropertyName("blockingIssues")]
    public List<string> BlockingIssues { get; set; } = [];
}