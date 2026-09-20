using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

/// <summary>
/// Common contract required on every structured agent result (section 11).
/// The JSON produced by the LLM must deserialize into one of these typed results.
/// </summary>
public abstract class AgentResultBase
{
    [JsonPropertyName("agentCode")]
    public string? AgentCode { get; set; }

    [JsonPropertyName("projectCode")]
    public string? ProjectCode { get; set; }

    /// <summary>COMPLETE | FAILED | NEEDS_REVIEW | HUMAN_REVIEW.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("blockingIssues")]
    public List<string> BlockingIssues { get; set; } = [];

    /// <summary>ADVANCE_GATE | ROLLBACK | PAUSE | REJECT | HUMAN_REVIEW.</summary>
    [JsonPropertyName("gateRecommendation")]
    public string? GateRecommendation { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
}