using System.Text.Json;
using System.Text.Json.Nodes;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Agents;

public sealed record AgentOutputValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static AgentOutputValidationResult Valid => new(true, []);
}

/// <summary>
/// Validates that a structured LLM output conforms to the typed contract of the
/// agent (section 11/40 flow). The control plane refuses malformed output.
/// </summary>
public interface IAgentOutputValidator
{
    AgentOutputValidationResult Validate(AgentCode agentCode, string serializedOutput);
    TResult? Deserialize<TResult>(string serializedOutput) where TResult : class;
}

public sealed class AgentOutputValidator : IAgentOutputValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly StringComparer CodeComparer = StringComparer.OrdinalIgnoreCase;

    public AgentOutputValidationResult Validate(AgentCode agentCode, string serializedOutput)
    {
        if (string.IsNullOrWhiteSpace(serializedOutput))
            return new AgentOutputValidationResult(false, ["Output is empty."]);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(serializedOutput)?.AsObject()
                   ?? throw new JsonException("Output is not a JSON object.");
        }
        catch (JsonException ex)
        {
            return new AgentOutputValidationResult(false, [$"Output is not valid JSON: {ex.Message}"]);
        }

        var errors = new List<string>();

        var agentCodeActual = GetString(root, "agentCode");
        if (agentCodeActual is null)
            errors.Add("Missing 'agentCode'.");
        else if (!CodeComparer.Equals(agentCodeActual, agentCode.ToString()))
            errors.Add($"Agent code mismatch: found '{agentCodeActual}', expected '{agentCode}'.");

        if (GetString(root, "projectCode") is null)
            errors.Add("Missing 'projectCode'.");

        var status = GetString(root, "status");
        if (status is null)
        {
            errors.Add("Missing 'status'.");
        }
        else if (status is not ("COMPLETE" or "FAILED" or "NEEDS_REVIEW" or "HUMAN_REVIEW"))
        {
            errors.Add($"Unknown status '{status}'. Expected COMPLETE, FAILED, NEEDS_REVIEW or HUMAN_REVIEW.");
        }

        if (root["blockingIssues"] is not null && root["blockingIssues"] is not JsonArray)
            errors.Add("'blockingIssues' must be an array.");

        if (GetString(root, "summary") is null)
            errors.Add("Missing 'summary'.");

        if (status == "COMPLETE")
            AddAgentSpecificChecks(agentCode, root, errors);

        return errors.Count == 0 ? AgentOutputValidationResult.Valid : new AgentOutputValidationResult(false, errors);
    }

    public TResult? Deserialize<TResult>(string serializedOutput) where TResult : class =>
        JsonSerializer.Deserialize<TResult>(serializedOutput, Options);

    private static void AddAgentSpecificChecks(AgentCode agentCode, JsonObject root, List<string> errors)
    {
        switch (agentCode)
        {
            case AgentCode.Scout:
                if (root["niches"] is JsonArray niches && niches.Count == 0)
                    errors.Add("Scout result is COMPLETE but 'niches' is empty.");
                break;

            case AgentCode.Validator:
                if (root["concepts"] is JsonArray concepts && concepts.Count == 0)
                    errors.Add("Validator result is COMPLETE but 'concepts' is empty.");
                break;

            case AgentCode.Architect:
                if (root["chapters"] is JsonArray chapters && chapters.Count == 0)
                    errors.Add("Architect result is COMPLETE but 'chapters' is empty.");
                break;

            case AgentCode.Writer:
                if (root["sections"] is JsonArray sections && sections.Count == 0)
                    errors.Add("Writer result is COMPLETE but 'sections' is empty.");
                break;

            case AgentCode.ArtDirector:
                if (root["styleGuide"] is null)
                    errors.Add("ArtDirector result is COMPLETE but 'styleGuide' is missing.");
                break;

            case AgentCode.Production:
                if (root["dimensions"] is null)
                    errors.Add("Production result is COMPLETE but 'dimensions' is missing.");
                break;

            case AgentCode.Metadata:
                if (root["title"] is null)
                    errors.Add("Metadata result is COMPLETE but 'title' is missing.");
                break;

            case AgentCode.Qa:
                if (root["qaPassed"] is null)
                    errors.Add("Qa result is COMPLETE but boolean 'qaPassed' is missing.");
                break;

            case AgentCode.Launch:
                if (root["publishDate"] is null)
                    errors.Add("Launch result is COMPLETE but 'publishDate' is missing.");
                break;
        }
    }

    private static string? GetString(JsonObject root, string property) =>
        root[property]?.GetValue<string>();
}