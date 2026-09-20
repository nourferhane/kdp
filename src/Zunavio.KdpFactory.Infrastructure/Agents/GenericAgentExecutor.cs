using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;

namespace Zunavio.KdpFactory.Infrastructure.Agents;

/// <summary>
/// Generic agent executor that assembles the model call and maps the
/// structured output to a validated <see cref="AgentExecutionResult"/>.
/// The control plane (OrchestrationEngine + AgentOutputValidator) performs
/// the authoritative validation; this class only performs the extraction
/// required to route the result.
/// </summary>
public sealed class GenericAgentExecutor : IAgentExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private readonly ILanguageModelClient _modelClient;
    private readonly ILogger<GenericAgentExecutor> _logger;

    public GenericAgentExecutor(ILanguageModelClient modelClient, ILogger<GenericAgentExecutor> logger)
    {
        _modelClient = modelClient;
        _logger = logger;
    }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken ct)
    {
        var systemMessage = new LanguageModelMessage
        {
            Role = "system",
            Content = context.PromptSnapshot,
        };

        var userMessage = new LanguageModelMessage
        {
            Role = "user",
            Content = AgentContextJsonBuilder.Build(context),
        };

        var modelResult = await _modelClient.CompleteStructuredAsync(new LanguageModelRequest
        {
            Model = context.Model,
            Messages = [systemMessage, userMessage],
        }, ct);

        return ExtractResult(context, modelResult.StructuredOutput.ToJsonString(SerializerOptions), modelResult);
    }

    private AgentExecutionResult ExtractResult(
        AgentExecutionContext context,
        string normalizedOutput,
        LanguageModelResult modelResult)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(normalizedOutput)!.AsObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Agent output is not a valid JSON object: {ex.Message}", ex);
        }

        var status = GetString(root, "status")
            ?? throw new InvalidOperationException("Agent output is missing the required 'status' field.");
        var summary = GetString(root, "summary") ?? string.Empty;
        var gateRecommendation = GetString(root, "gateRecommendation") ?? GetString(root, "gate_recommendation");
        var blockingIssues = new List<string>();
        if (root["blockingIssues"] is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
                    blockingIssues.Add(text);
            }
        }

        _logger.LogDebug("Agent {AgentCode} execution extracted (status={Status}, gate={Gate}, issues={Issues}).",
            context.AgentDefinition.Code, status, gateRecommendation, blockingIssues.Count);

        return new AgentExecutionResult
        {
            AgentCode = context.AgentDefinition.Code,
            ProjectCode = context.Project.ProjectCode,
            Status = status,
            BlockingIssues = blockingIssues,
            GateRecommendation = gateRecommendation,
            Summary = summary,
            OutputJson = normalizedOutput,
            Metadata = new AgentRunMetadata
            {
                Model = modelResult.Model,
                OpenAiRequestId = modelResult.RequestId,
                InputTokens = modelResult.InputTokens,
                OutputTokens = modelResult.OutputTokens,
                EstimatedCost = modelResult.EstimatedCost,
            },
        };
    }

    private static string? GetString(JsonObject root, string key)
    {
        if (root[key] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        // Case-insensitive fallback (agents sometimes capitalise keys).
        foreach (var pair in root)
        {
            if (Ci.Equals(pair.Key, key) && pair.Value is JsonValue v && v.TryGetValue<string>(out var t))
                return t;
        }

        return null;
    }
}