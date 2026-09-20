using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Gemini;

public sealed class GeminiLanguageModelClient : ILanguageModelClient
{
    private static readonly HttpClient Http = new();

    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiLanguageModelClient> _logger;

    public GeminiLanguageModelClient(
        IOptions<GeminiOptions> options,
        ILogger<GeminiLanguageModelClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LanguageModelResult> CompleteStructuredAsync(
        LanguageModelRequest request,
        CancellationToken ct)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException($"Gemini is not configured. Set '{KdpSettings.GeminiApiKeyEnvKey}'.");

        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model;
        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];

        var payload = BuildPayload(request, model);
        var body = payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds)));

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        string responseText = string.Empty;

        for (var attempt = 0; attempt <= Math.Max(0, _options.MaxRetries); attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");

            try
            {
                response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Gemini completion timed out after {_options.TimeoutSeconds}s for model '{model}'.");
            }

            if (response.IsSuccessStatusCode)
                break;

            if (!IsTransient(response.StatusCode) || attempt >= _options.MaxRetries)
                throw new InvalidOperationException(
                    $"Gemini API failure ({(int)response.StatusCode} {response.StatusCode}) for model '{model}': {Truncate(responseText, 1200)}");

            response.Dispose();
            response = null;
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, attempt))), timeoutCts.Token);
        }

        using (response)
        {
            if (response is null)
                throw new InvalidOperationException("Gemini API returned no response.");

            JsonNode root;
            try
            {
                root = JsonNode.Parse(responseText) ?? throw new JsonException("Null response.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Gemini returned invalid response JSON: {ex.Message}", ex);
            }

            var candidate = root["candidates"]?[0];
            var parts = candidate?["content"]?["parts"]?.AsArray();
            var generated = parts is null
                ? null
                : string.Concat(parts.Select(p => p?["text"]?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)));

            if (string.IsNullOrWhiteSpace(generated))
            {
                var blockReason = root["promptFeedback"]?["blockReason"]?.GetValue<string>();
                var finishReason = candidate?["finishReason"]?.GetValue<string>();
                throw new InvalidOperationException(
                    $"Gemini returned no text for model '{model}'. blockReason={blockReason ?? "none"}, finishReason={finishReason ?? "unknown"}.");
            }

            JsonNode structured;
            try
            {
                structured = JsonNode.Parse(StripJsonFence(generated)) ?? throw new JsonException("Null JSON output.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Gemini returned non-JSON content for model '{model}': {ex.Message}. " +
                    $"Response starts with: {Truncate(generated.Trim(), 240)}",
                    ex);
            }

            var usage = root["usageMetadata"];
            var inputTokens = ReadInt(usage?["promptTokenCount"]);
            var outputTokens = ReadInt(usage?["candidatesTokenCount"]);
            var estimatedCost =
                (inputTokens ?? 0) / 1_000_000m * (decimal)(_options.PricePerMillionInput ?? 0) +
                (outputTokens ?? 0) / 1_000_000m * (decimal)(_options.PricePerMillionOutput ?? 0);

            var requestId = TryGetHeader(response, "x-request-id") ?? TryGetHeader(response, "x-goog-request-id");
            stopwatch.Stop();

            _logger.LogDebug(
                "Gemini completion ok for '{Model}' (in={In}, out={Out}, cost={Cost:G6}, req={Req})",
                model, inputTokens, outputTokens, estimatedCost, requestId);

            return new LanguageModelResult
            {
                StructuredOutput = structured,
                Model = model,
                RequestId = requestId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                EstimatedCost = estimatedCost > 0 ? estimatedCost : null,
                Duration = stopwatch.Elapsed,
            };
        }
    }

    private static JsonObject BuildPayload(LanguageModelRequest request, string model)
    {
        var root = new JsonObject();

        var systemText = string.Join(
            "\n\n",
            request.Messages
                .Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Content));

        if (!string.IsNullOrWhiteSpace(systemText))
        {
            root["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = systemText })
            };
        }

        var contents = new JsonArray();
        foreach (var message in request.Messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
        {
            contents.Add(new JsonObject
            {
                ["role"] = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = message.Content })
            });
        }
        root["contents"] = contents;

        var generation = new JsonObject
        {
            ["temperature"] = 0.4,
        };

        if (request.MaxTokens is > 0)
            generation["maxOutputTokens"] = request.MaxTokens.Value;

        var schema = string.IsNullOrWhiteSpace(request.ResponseSchemaJson)
            ? null
            : JsonNode.Parse(request.ResponseSchemaJson);

        // Gemini 3.x uses the newer responseFormat.text contract for
        // structured output. Gemini 2.x keeps the legacy responseMimeType /
        // responseJsonSchema fields. Keeping both paths lets Railway switch
        // models by environment variable without changing application code.
        if (UsesResponseFormat(model))
        {
            var textFormat = new JsonObject
            {
                ["mimeType"] = "APPLICATION_JSON",
            };

            if (schema is not null)
                textFormat["schema"] = schema;

            generation["responseFormat"] = new JsonObject
            {
                ["text"] = textFormat,
            };
        }
        else
        {
            generation["responseMimeType"] = "application/json";

            if (schema is not null)
                generation["responseJsonSchema"] = schema;
        }

        root["generationConfig"] = generation;
        return root;
    }

    private static bool UsesResponseFormat(string model)
    {
        var normalized = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model["models/".Length..]
            : model;

        return normalized.StartsWith("gemini-3.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.Conflict
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static int? ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string StripJsonFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
            return trimmed;

        var withoutOpen = trimmed[(firstLineEnd + 1)..];
        var close = withoutOpen.LastIndexOf("```", StringComparison.Ordinal);
        return close >= 0 ? withoutOpen[..close].Trim() : withoutOpen.Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
