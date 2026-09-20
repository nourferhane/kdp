using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class LaunchResult : AgentResultBase
{
    [JsonPropertyName("isbnAssignment")]
    public string? IsbnAssignment { get; set; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; set; }

    [JsonPropertyName("listingUrl")]
    public string? ListingUrl { get; set; }

    [JsonPropertyName("launchPlan")]
    public List<LaunchStep> LaunchPlan { get; set; } = [];

    [JsonPropertyName("measurementPlan")]
    public List<string> MeasurementPlan { get; set; } = [];
}

public sealed class LaunchStep
{
    [JsonPropertyName("step")]
    public string? Step { get; set; }

    [JsonPropertyName("when")]
    public string? When { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}