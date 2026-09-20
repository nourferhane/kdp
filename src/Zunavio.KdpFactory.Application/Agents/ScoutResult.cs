using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class ScoutResult : AgentResultBase
{
    [JsonPropertyName("marketOverview")]
    public MarketOverview? MarketOverview { get; set; }

    [JsonPropertyName("niches")]
    public List<MarketNiche> Niches { get; set; } = [];

    [JsonPropertyName("competitorInsights")]
    public List<string> CompetitorInsights { get; set; } = [];
}

public sealed class MarketOverview
{
    [JsonPropertyName("segment")]
    public string? Segment { get; set; }

    [JsonPropertyName("demandSignal")]
    public string? DemandSignal { get; set; }

    [JsonPropertyName("entryBarrier")]
    public string? EntryBarrier { get; set; }

    [JsonPropertyName("recommendedBookType")]
    public string? RecommendedBookType { get; set; }
}

public sealed class MarketNiche
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("competingTitles")]
    public int? CompetingTitles { get; set; }

    [JsonPropertyName("avgRating")]
    public double? AvgRating { get; set; }

    [JsonPropertyName("pricePoint")]
    public string? PricePoint { get; set; }

    [JsonPropertyName("differentiationAngle")]
    public string? DifferentiationAngle { get; set; }
}