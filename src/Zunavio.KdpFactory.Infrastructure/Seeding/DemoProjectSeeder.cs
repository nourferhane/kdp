using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Persistence;

namespace Zunavio.KdpFactory.Infrastructure.Seeding;

/// <summary>
/// Seeds the first real project — ZNV-001 "Winter Quest — The Lost Snowflake
/// Compass" — already sitting at the MANUSCRIPT gate with an approved Product
/// Architecture (section 34/35 bootstrapping). Executing the Writer next
/// continues exactly where production left off. Idempotent by project code.
/// </summary>
public sealed class DemoProjectSeeder
{
    public const string ProjectCode = "ZNV-001";

    private static readonly string ArchitectureDocId = "1xJq1bzh19oHwqpW3-j76uk1mIu7mh0HH7awFnkQFVZI";
    private static readonly string DriveFolderId = "1Kb_KRRkEucBCFV8PpM7vXVLYMXPl7lDQ";

    private readonly KdpDbContext _db;
    private readonly ILogger<DemoProjectSeeder> _logger;

    public DemoProjectSeeder(
        KdpDbContext db,
        ILogger<DemoProjectSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> EnsureZnv001Async(CancellationToken ct)
    {
        if (await _db.Projects.AnyAsync(p => p.ProjectCode == ProjectCode, ct))
        {
            return false;
        }

        var architect = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Code == nameof(AgentCode.Architect), ct);
        if (architect is null)
        {
            _logger.LogWarning("Cannot seed {ProjectCode}: no Architect agent definition. Run seeding first.", ProjectCode);
            return false;
        }

        var (architectJson, runCode) = BuildArchitectRun();

        var project = new Project
        {
            ProjectCode = ProjectCode,
            WorkingTitle = "Winter Quest — The Lost Snowflake Compass",
            FinalTitle = "Winter Quest: The Lost Snowflake Compass",
            Marketplace = "Amazon.com",
            Language = "English",
            TargetAge = "6-9",
            BookType = "Story + Puzzle Activity Book",
            Season = "Winter / Evergreen",
            MarketScore = 88,
            CurrentGate = ProjectGate.Manuscript,
            Status = ProjectStatus.Active,
            CurrentManuscriptVersion = string.Empty,
            DriveFolderId = "1Kb_KRRkEucBCFV8PpM7vXVLYMXPl7lDQ",
            DriveFolderUrl = $"https://drive.google.com/drive/folders/{DriveFolderId}",
            NextAction = $"RUN_{nameof(AgentCode.Writer).ToUpperInvariant()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var run = new AgentRun
        {
            RunCode = runCode,
            ProjectId = project.Id,
            AgentDefinitionId = architect.Id,
            Status = AgentRunStatus.Complete,
            StartedAt = DateTime.UtcNow.AddDays(-2),
            CompletedAt = DateTime.UtcNow.AddDays(-2),
            PromptSnapshot = "(seeded from approved architecture document)",
            PromptVersion = "v1.0",
            PromptHash = "seeded",
            OutputVersion = "v1.0",
            OutputJson = architectJson,
            Summary = "Approved architecture for Winter Quest: 12 chapters, ~4200 words, puzzle-regulation 1 puzzle per 4 pages, character designs (Ollie/Nala Grundy), visual bible and USP plan approved.",
            GateRecommendation = nameof(OrchestratorDecisionKind.AdvanceGate),
            IdempotencyKey = $"{project.Id}:{ProjectGate.Architecture}:{nameof(AgentCode.Architect)}",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        };

        var asset = new Asset
        {
            AssetCode = $"{ProjectCode}_PRODUCTARCHITECTURE_v1.0",
            ProjectId = project.Id,
            AssetType = AssetType.ProductArchitecture,
            Version = "v1.0",
            DriveFileId = ArchitectureDocId,
            DriveUrl = $"https://docs.google.com/document/d/{ArchitectureDocId}/edit",
            Status = AssetStatus.Approved,
            QaStatus = AssetQaStatus.NotChecked,
            CreatedByAgentRunId = run.Id,
            ContentJson = architectJson,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        };

        project.Assets.Add(asset);
        project.Runs.Add(run);

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seeded {ProjectCode} at {Gate} (ACTIVE) with approved {Asset}; next action: {NextAction}.",
            ProjectCode, project.CurrentGate, asset.AssetCode, project.NextAction);

        return true;
    }

    private static (string ArchitectJson, string RunCode) BuildArchitectRun()
    {
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["agentCode"] = nameof(AgentCode.Architect),
            ["projectCode"] = ProjectCode,
            ["status"] = "COMPLETE",
            ["blockingIssues"] = new System.Text.Json.Nodes.JsonArray(),
            ["summary"] = "Architecture approved and captured in the source-of-truth document.",
            ["gateRecommendation"] = nameof(OrchestratorDecisionKind.AdvanceGate),
            ["architectureVersion"] = "v1.0",
            ["workingTitle"] = "Winter Quest — The Lost Snowflake Compass",
            ["finalTitle"] = "Winter Quest: The Lost Snowflake Compass",
            ["chapters"] = new System.Text.Json.Nodes.JsonArray(
                Enumerable.Range(1, 12).Select(i =>
                {
                    var chapter = (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                    {
                        ["chapterId"] = $"CH{i:D2}",
                        ["chapterTitle"] = $"Chapter {i}",
                        ["pageCount"] = 10,
                        ["objective"] = "advance quest",
                    };
                    if (i % 3 == 0)
                    {
                        chapter["puzzleSpec"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "maze",
                            ["title"] = $"Puzzle {i / 3}",
                            ["page"] = i * 10 - 2,
                        };
                    }

                    return chapter;
                }).ToArray()),
            ["scope"] = new System.Text.Json.Nodes.JsonObject
            {
                ["pageCount"] = 96,
                ["trimSize"] = "8.5 x 8.5 in",
                ["targetWordCount"] = 4200,
                ["illustrationCount"] = 48,
                ["puzzleRegulation"] = "1 puzzle per 4 pages; every puzzle solvable and machine-checkable.",
            },
            ["characterDesigns"] = new System.Text.Json.Nodes.JsonObject
            {
                ["protagonist"] = "Ollie Foxling",
                ["sidekicks"] = new System.Text.Json.Nodes.JsonArray("Nala Snowhare", "Bramble the polar bear cub"),
                ["antagonist"] = "Grundy the Glacier King",
            },
            ["visualBibleGuidelines"] = new System.Text.Json.Nodes.JsonObject
            {
                ["palette"] = "frost blues, warm ambers, deep teal",
                ["illustrationStyle"] = "cozy storybook, soft rounded shapes",
                ["typography"] = "clean sans for headings, friendly rounded sans for body",
                ["consistency"] = "character sheets locked in the Product Architecture doc",
                ["sampleScene"] = "Ollie crossing the Whisper Ice Bridge",
            },
            ["uniformSystem"] = new System.Text.Json.Nodes.JsonObject
            {
                ["languageRules"] = "present tense, target age 6-9, short sentences",
                ["tone"] = "warm, adventurous, never scary",
                ["recurringElements"] = "compass, snowflakes, brambles",
                ["easterEggEnd"] = "the compass reappears as a teaser for the next book",
                ["nextBookSetup"] = "The Sunken Amber Compass",
            },
            ["uniqueSellingPoints"] = new System.Text.Json.Nodes.JsonArray(
                "solvable puzzles with solutions", "consistent visual bible", "evergreen winter theme"),
            ["sourceOfTruthNote"] = "This JSON mirrors the approved architecture document in Google Drive.",
        };

        var jsonText = json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        return (jsonText, $"{ProjectCode}_RUN_012");
    }
}