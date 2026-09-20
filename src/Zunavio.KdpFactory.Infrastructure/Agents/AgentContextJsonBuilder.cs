using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Infrastructure.Agents;

/// <summary>
/// Builds the user-facing context JSON for a single agent execution.
/// Only approved, relevant assets and predecessor outputs are included.
/// </summary>
public static class AgentContextJsonBuilder
{
    private static JsonSerializerOptions Compact => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(AgentExecutionContext context)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["promptVersion"] = context.PromptVersion,
            ["promptHash"] = context.PromptHash,
            ["idempotencyKey"] = context.IdempotencyKey,
            ["inputVersion"] = context.InputVersion,
            ["agent"] = new JsonObject
            {
                ["code"] = context.AgentDefinition.Code,
                ["name"] = context.AgentDefinition.Name,
                ["phase"] = "single-agent-execution",
            },
            ["factorySettings"] = new JsonObject
            {
                ["marketplace"] = context.FactorySettings.Marketplace,
                ["language"] = context.FactorySettings.Language,
                ["marketplaceConstraints"] = new JsonArray(context.FactorySettings.MarketplaceConstraints
                    .Select(c => (JsonNode)JsonValue.Create(c)).ToArray()),
            },
            ["project"] = new JsonObject
            {
                ["projectCode"] = context.Project.ProjectCode,
                ["workingTitle"] = context.Project.WorkingTitle,
                ["finalTitle"] = context.Project.FinalTitle,
                ["marketplace"] = context.Project.Marketplace,
                ["language"] = context.Project.Language,
                ["targetAge"] = context.Project.TargetAge,
                ["bookType"] = context.Project.BookType,
                ["season"] = context.Project.Season,
                ["marketScore"] = context.Project.MarketScore,
                ["currentGate"] = context.Project.CurrentGate.ToString(),
            },
            ["approvedAssets"] = new JsonArray(context.ApprovedAssets.Select(a => (JsonNode)new JsonObject
            {
                ["assetType"] = a.AssetType.ToString(),
                ["assetCode"] = a.AssetCode,
                ["version"] = a.Version,
                ["driveUrl"] = a.DriveUrl,
                ["content"] = TryParse(a.ContentJson),
            }).ToArray()),
            ["previousAgentOutputs"] = new JsonObject(context.PreviousAgentOutputs
                .ToDictionary(pair => pair.Key,
                    pair => (JsonNode)TryParse(pair.Value) ?? JsonValue.Create(pair.Value)!)),
            ["responseContract"] = """
                Return a single JSON object (no markdown, no explanation outside the object) with
                these top-level keys:
                - "agentCode": your code ("Writer", "Architect", ...),
                - "projectCode": the project code,
                - "status": one of "COMPLETE", "FAILED", "NEEDS_REVIEW", "HUMAN_REVIEW",
                - "blockingIssues": array of strings describing anything that blocks completion,
                - "summary": one-paragraph summary of what you produced,
                - "gateRecommendation": one of "ADVANCE_GATE", "ROLLBACK", "PAUSE", "REJECT", "HUMAN_REVIEW".
                Then include the full body of your specialist deliverable described in the system prompt
                (e.g. Writer: "title", "sections"[], "puzzles"[]; QA: "qaPassed" and "issues"[]).
                """,
        };

        return root.ToJsonString(Compact);
    }

    private static JsonNode? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }
}