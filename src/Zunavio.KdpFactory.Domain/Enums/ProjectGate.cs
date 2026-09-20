namespace Zunavio.KdpFactory.Domain.Enums;

/// <summary>
/// The deterministic workflow gates of a KDP publishing pipeline.
/// The numeric order mirrors the legal forward sequence.
/// </summary>
public enum ProjectGate
{
    None = 0,
    Idea = 1,
    MarketResearch = 2,
    MarketValidation = 3,
    Architecture = 4,
    Manuscript = 5,
    VisualProduction = 6,
    BookProduction = 7,
    Metadata = 8,
    Qa = 9,
    ReadyToPublish = 10,
    Published = 11,
}