using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class ArchitectResult : AgentResultBase
{
    [JsonPropertyName("architectureVersion")]
    public string? ArchitectureVersion { get; set; }

    [JsonPropertyName("selectedConceptId")]
    public string? SelectedConceptId { get; set; }

    [JsonPropertyName("targetAudience")]
    public TargetAudience? TargetAudience { get; set; }

    [JsonPropertyName("bookSpec")]
    public BookSpec? BookSpec { get; set; }

    [JsonPropertyName("chapters")]
    public List<ChapterSpec> Chapters { get; set; } = [];

    [JsonPropertyName("visualDirection")]
    public string? VisualDirection { get; set; }
}

public sealed class TargetAudience
{
    [JsonPropertyName("ageRange")]
    public string? AgeRange { get; set; }

    [JsonPropertyName("readingLevel")]
    public string? ReadingLevel { get; set; }

    [JsonPropertyName("motivations")]
    public List<string> Motivations { get; set; } = [];
}

public sealed class BookSpec
{
    [JsonPropertyName("trimSize")]
    public string? TrimSize { get; set; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; set; }

    [JsonPropertyName("coverType")]
    public string? CoverType { get; set; }

    [JsonPropertyName("interiorColor")]
    public string? InteriorColor { get; set; }

    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }

    [JsonPropertyName("bleed")]
    public string? Bleed { get; set; }
}

public sealed class ChapterSpec
{
    [JsonPropertyName("chapterNumber")]
    public int ChapterNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }
}