using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.OpenAi;

/// <summary>
/// OpenAI implementation of <see cref="ILanguageModelClient"/> using the
/// official OpenAI .NET SDK with JSON-object / JSON-schema structured output.
/// SDK-internal retry policy covers 429 + server errors; this class adds a
/// strict timeout and keeps model/price concerns in <see cref="OpenAiOptions"/>.
/// </summary>
public sealed class OpenAiLanguageModelClient : ILanguageModelClient
{
    private readonly OpenAIClient _client;
    private readonly ILogger<OpenAiLanguageModelClient> _logger;
    private readonly string _defaultModel;
    private readonly double[] _pricePerMillion;
    private readonly TimeSpan _timeout;

    public OpenAiLanguageModelClient(IOptions<OpenAiOptions> options, ILogger<OpenAiLanguageModelClient> logger)
    {
        _logger = logger;
        _defaultModel = options.Value.Model;
        _timeout = TimeSpan.FromSeconds(Math.Max(10, options.Value.TimeoutSeconds));

        _pricePerMillion =
        [
            options.Value.PricePerMillionInput ?? 0,
            options.Value.PricePerMillionOutput ?? 0
        ];

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.Value.BaseUrl))
        {
            clientOptions.Endpoint = new Uri(options.Value.BaseUrl);
        }

        var key = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            _client = null!;
            return;
        }

        _client = new OpenAIClient(new ApiKeyCredential(key), clientOptions);
    }

    public async Task<LanguageModelResult> CompleteStructuredAsync(LanguageModelRequest request, CancellationToken ct)
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                $"OpenAI is not configured. Set '{KdpSettings.OpenAiApiKeyEnvKey}'.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model;
        var messages = request.Messages.Select(ToChatMessage).ToList();
        var completionOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = request.MaxTokens,
            Temperature = 0.4f,
            ResponseFormat = request.ResponseSchemaJson is null
                ? ChatResponseFormat.CreateJsonObjectFormat()
                : ChatResponseFormat.CreateJsonSchemaFormat(
                    request.ResponseSchemaName ?? "factory_output",
                    BinaryData.FromString(request.ResponseSchemaJson),
                    "Structured output for the KDP factory agent.")
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        ChatCompletion completion;
        try
        {
            completion = await _client.GetChatClient(model)
                .CompleteChatAsync(messages, completionOptions, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"OpenAI completion timed out after {_timeout.TotalSeconds:0}s for model '{model}'.");
        }
        catch (ClientResultException ex) when (IsTransient(ex))
        {
            // SDK retries were exhausted; surface a transient failure the job
            // worker will retry with its own backoff.
            throw new HttpRequestException(
                $"OpenAI transient failure ({ex.Status}) for model '{model}': {ex.Message}", ex);
        }

        if (completion.Content is null || completion.Content.Count == 0)
        {
            throw new InvalidOperationException(
                $"OpenAI returned no content for model '{model}'. Refusal: " +
                (completion?.Refusal ?? "unknown"));
        }

        var text = completion.Content[0].Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"OpenAI returned empty content for model '{model}'. Refusal: {completion.Refusal}");
        }

        JsonNode json;
        try
        {
            json = JsonNode.Parse(text) ?? throw new JsonException("Null JSON document.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"OpenAI returned non-JSON content for model '{model}': {ex.Message}", ex);
        }

        var usage = completion.Usage;
        var inputTokens = usage?.InputTokenCount;
        var outputTokens = usage?.OutputTokenCount;

        var estimatedCost = (
            (inputTokens ?? 0) / 1_000_000m * (decimal)_pricePerMillion[0] +
            (outputTokens ?? 0) / 1_000_000m * (decimal)_pricePerMillion[1]);

        _logger.LogDebug("OpenAI completion ok for '{Model}' (in={In}, out={Out}, cost={Cost:G6}, req={Req})",
            model, inputTokens, outputTokens, estimatedCost, completion.Id);

        return new LanguageModelResult
        {
            StructuredOutput = json,
            Model = model,
            RequestId = completion.Id,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCost = estimatedCost > 0 ? estimatedCost : null,
            Duration = TimeSpan.Zero,
        };
    }

    private static ChatMessage ToChatMessage(LanguageModelMessage message) => message.Role switch
    {
        "system" => new SystemChatMessage(message.Content),
        "assistant" => new AssistantChatMessage(message.Content),
        _ => new UserChatMessage(message.Content),
    };

    private static bool IsTransient(ClientResultException ex) =>
        ex.Status is 408 or 409 or 429 or 500 or 502 or 503 or 504;
}