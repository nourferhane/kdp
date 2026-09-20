using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class ValidatorResult : AgentResultBase
{
    [JsonPropertyName("concepts")]
    public List<ValidatedConcept> Concepts { get; set; } = [];
}

public sealed class ValidatedConcept
{
    [JsonPropertyName("conceptId")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("positioning")]
    public string? Positioning { get; set; }

    [JsonPropertyName("marketScore")]
    public int MarketScore { get; set; }

    [JsonPropertyName("riskNotes")]
    public string? RiskNotes { get; set; }

    [JsonPropertyName("copy")]
    public string? Copy { get; set; }
}