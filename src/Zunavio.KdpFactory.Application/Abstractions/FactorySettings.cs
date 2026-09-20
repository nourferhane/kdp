namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// Non-secret factory-wide configuration distilled from environment
/// configuration and passed to agents as part of their context.
/// </summary>
public sealed record FactorySettings
{
    public required string Marketplace { get; init; }
    public required string Language { get; init; }

    /// <summary>Hard guardrails the Scout/Validator must respect (e.g. banned categories).</summary>
    public IReadOnlyList<string> MarketplaceConstraints { get; init; } = [];

    public string AiProvider { get; init; } = "OpenAI";

    public required string DefaultModel { get; init; }

    /// <summary>Whether the AI Orchestrator step runs after each specialist run.</summary>
    public bool EnableAiOrchestrator { get; init; } = true;

    /// <summary>Max retries for transient agent failures.</summary>
    public int MaxAgentRetries { get; init; } = 3;

    /// <summary>Page size of the control center import.</summary>
    public int GooglePagesPerCall { get; init; } = 100;

    /// <summary>Whether Google integration is configured. When false, Drive saves are skipped but the pipeline still runs.</summary>
    public bool GoogleEnabled { get; init; }
}

public interface IFactorySettingsProvider
{
    Task<FactorySettings> GetAsync(CancellationToken cancellationToken = default);
}