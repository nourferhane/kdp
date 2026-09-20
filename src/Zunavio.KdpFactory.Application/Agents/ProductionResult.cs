using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed class ProductionResult : AgentResultBase
{
    [JsonPropertyName("productionVersion")]
    public string? ProductionVersion { get; set; }

    [JsonPropertyName("interiorPdf")]
    public FileReference? InteriorPdf { get; set; }

    [JsonPropertyName("coverPdf")]
    public FileReference? CoverPdf { get; set; }

    [JsonPropertyName("epub")]
    public FileReference? Epub { get; set; }

    [JsonPropertyName("dimensions")]
    public ProductionDimensions? Dimensions { get; set; }
}

public sealed class FileReference
{
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("bytes")]
    public int? Bytes { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("driveFileId")]
    public string? DriveFileId { get; set; }

    [JsonPropertyName("driveUrl")]
    public string? DriveUrl { get; set; }
}

public sealed class ProductionDimensions
{
    [JsonPropertyName("pages")]
    public string? Pages { get; set; }

    [JsonPropertyName("trimSize")]
    public string? TrimSize { get; set; }

    [JsonPropertyName("bleed")]
    public string? Bleed { get; set; }
}