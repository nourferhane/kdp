using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class ArtDirectorResult : AgentResultBase
{
    [JsonPropertyName("visualBibleVersion")]
    public string? VisualBibleVersion { get; set; }

    [JsonPropertyName("characterDesigns")]
    public List<CharacterDesign> CharacterDesigns { get; set; } = [];

    [JsonPropertyName("environmentDesigns")]
    public List<EnvironmentDesign> EnvironmentDesigns { get; set; } = [];

    [JsonPropertyName("palette")]
    public Palette? Palette { get; set; }

    [JsonPropertyName("typography")]
    public string? Typography { get; set; }

    [JsonPropertyName("styleGuide")]
    public string? StyleGuide { get; set; }

    [JsonPropertyName("imageGenerationPlan")]
    public ImageGenerationPlan? ImageGenerationPlan { get; set; }
}

public sealed class CharacterDesign
{
    [JsonPropertyName("characterId")]
    public string? CharacterId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("promptSeed")]
    public string? PromptSeed { get; set; }
}

public sealed class EnvironmentDesign
{
    [JsonPropertyName("environmentId")]
    public string? EnvironmentId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("promptSeed")]
    public string? PromptSeed { get; set; }
}

public sealed class Palette
{
    [JsonPropertyName("primary")]
    public string? Primary { get; set; }

    [JsonPropertyName("secondary")]
    public string? Secondary { get; set; }

    [JsonPropertyName("accent")]
    public string? Accent { get; set; }

    [JsonPropertyName("mood")]
    public string? Mood { get; set; }
}

public sealed class ImageGenerationPlan
{
    [JsonPropertyName("assetsNeeded")]
    public int AssetsNeeded { get; set; }

    [JsonPropertyName("estimatedUnitCost")]
    public decimal? EstimatedUnitCost { get; set; }

    [JsonPropertyName("estimatedTotalCost")]
    public decimal? EstimatedTotalCost { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}