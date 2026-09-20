using System.Text.Json.Serialization;

namespace Zunavio.KdpFactory.Application.Agents;

/// <summary>
/// Writer output. ZNV-001 is an activity book, so the output is structured
/// into sections and puzzles rather than a single opaque manuscript string
/// (section 36). ArtDirector and Qa consume the structured form.
/// </summary>
public sealed class WriterResult : AgentResultBase
{
    [JsonPropertyName("manuscriptVersion")]
    public string? ManuscriptVersion { get; set; }

    [JsonPropertyName("workingTitle")]
    public string? WorkingTitle { get; set; }

    [JsonPropertyName("sections")]
    public List<ManuscriptSection> Sections { get; set; } = [];

    [JsonPropertyName("puzzles")]
    public List<PuzzleSpec> Puzzles { get; set; } = [];

    [JsonPropertyName("continuityChecks")]
    public List<ContinuityCheck> ContinuityChecks { get; set; } = [];

    [JsonPropertyName("issuesForArchitect")]
    public List<string> IssuesForArchitect { get; set; } = [];
}

public sealed class ManuscriptSection
{
    [JsonPropertyName("sectionId")]
    public string? SectionId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("pageStart")]
    public int? PageStart { get; set; }

    [JsonPropertyName("pageEnd")]
    public int? PageEnd { get; set; }

    [JsonPropertyName("narrative")]
    public string? Narrative { get; set; }

    [JsonPropertyName("artNotes")]
    public string? ArtNotes { get; set; }
}

public sealed class PuzzleSpec
{
    [JsonPropertyName("puzzleId")]
    public string? PuzzleId { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("puzzleType")]
    public string? PuzzleType { get; set; }

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("storyContext")]
    public string? StoryContext { get; set; }

    [JsonPropertyName("instruction")]
    public string? Instruction { get; set; }

    [JsonPropertyName("puzzleContent")]
    public string? PuzzleContent { get; set; }

    [JsonPropertyName("correctAnswer")]
    public string? CorrectAnswer { get; set; }

    [JsonPropertyName("persistentClue")]
    public string? PersistentClue { get; set; }

    [JsonPropertyName("artRequirements")]
    public string? ArtRequirements { get; set; }

    [JsonPropertyName("qaNotes")]
    public string? QaNotes { get; set; }

    [JsonPropertyName("answerPosition")]
    public string? AnswerPosition { get; set; }
}

public sealed class ContinuityCheck
{
    [JsonPropertyName("check")]
    public string? Check { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}