using System.Text.Json;
using System.Text.Json.Serialization;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Orchestration;

/// <summary>Structured payload attached to a HumanReviewRequest (the "context" of the review).</summary>
public sealed class ReviewPayload
{
    [JsonPropertyName("reviewType")]
    public string? ReviewType { get; set; }

    /// <summary>The gate to advance to once the review is approved (deferred action).</summary>
    [JsonPropertyName("targetGate")]
    public string? TargetGate { get; set; }

    /// <summary>Asset produced by the run, to be approved together with the gate advance.</summary>
    [JsonPropertyName("assetId")]
    public Guid? AssetId { get; set; }

    [JsonPropertyName("assetType")]
    public string? AssetType { get; set; }

    [JsonPropertyName("concepts")]
    public List<ConceptOption> Concepts { get; set; } = [];

    [JsonPropertyName("runId")]
    public Guid? RunId { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ReviewPayload Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ReviewPayload();
        try
        {
            return JsonSerializer.Deserialize<ReviewPayload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ReviewPayload();
        }
        catch (JsonException)
        {
            return new ReviewPayload();
        }
    }
}

public sealed class ConceptOption
{
    [JsonPropertyName("conceptId")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("marketScore")]
    public int MarketScore { get; set; }

    [JsonPropertyName("positioning")]
    public string? Positioning { get; set; }
}