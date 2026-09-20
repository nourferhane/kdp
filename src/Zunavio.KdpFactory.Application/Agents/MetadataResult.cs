using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class MetadataResult : AgentResultBase
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("series")]
    public string? Series { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    [JsonPropertyName("backCoverCopy")]
    public string? BackCoverCopy { get; set; }
}