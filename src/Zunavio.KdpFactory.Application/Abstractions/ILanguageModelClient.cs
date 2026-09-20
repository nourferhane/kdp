using System.Text.Json.Nodes;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>A serializable message for the model.</summary>
public sealed record LanguageModelMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }

    public static LanguageModelMessage System(string content) => new() { Role = "system", Content = content };
    public static LanguageModelMessage User(string content) => new() { Role = "user", Content = content };
    public static LanguageModelMessage Assistant(string content) => new() { Role = "assistant", Content = content };
}

public sealed record LanguageModelRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<LanguageModelMessage> Messages { get; init; }

    /// <summary>Optional JSON Schema (RFC draft) to enforce a JSON object output.</summary>
    public string? ResponseSchemaJson { get; init; }

    /// <summary>Optional label used as the schema name for structured output.</summary>
    public string? ResponseSchemaName { get; init; }

    public int? MaxTokens { get; init; }
}

public sealed record LanguageModelResult
{
    public required JsonNode StructuredOutput { get; init; }
    public required string Model { get; init; }
    public string? RequestId { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Abstraction over the language model provider. Keeps application code
/// isolated from SDK specifics (section 12).
/// </summary>
public interface ILanguageModelClient
{
    /// <summary>
    /// Runs a completion that must produce valid JSON. Resilience (timeouts,
    /// exponential backoff on transient failures) is applied; non-transient
    /// errors are surfaced immediately so they are never blindly retried.
    /// </summary>
    Task<LanguageModelResult> CompleteStructuredAsync(LanguageModelRequest request, CancellationToken ct);
}