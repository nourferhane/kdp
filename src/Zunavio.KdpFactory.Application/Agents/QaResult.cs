using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class QaResult : AgentResultBase
{
    [JsonPropertyName("qaPassed")]
    public bool QaPassed { get; set; }

    [JsonPropertyName("issues")]
    public List<QaIssue> Issues { get; set; } = [];

    [JsonPropertyName("verificationSteps")]
    public List<string> VerificationSteps { get; set; } = [];

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
}

public sealed class QaIssue
{
    [JsonPropertyName("issueId")]
    public string? IssueId { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("fixSuggestion")]
    public string? FixSuggestion { get; set; }
}