using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Agents;
using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// Executes a single agent: builds the model call for a given context and
/// returns a validated structured result. This is the boundary between the
/// control plane and AI execution (section 5 and 11).
/// </summary>
public interface IAgentExecutor
{
    Task<AgentExecutionResult> ExecuteAsync(AgentExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>The fully assembled and immutable input for one agent execution.</summary>
public sealed record AgentExecutionContext
{
    public required Project Project { get; init; }
    public required AgentDefinition AgentDefinition { get; init; }

    /// <summary>Immutable prompt text captured before the run.</summary>
    public required string PromptSnapshot { get; init; }

    public required string PromptVersion { get; init; }
    public required string PromptHash { get; init; }

    /// <summary>Approved assets relevant for this agent (never the full history).</summary>
    public required IReadOnlyList<AgentInputAsset> ApprovedAssets { get; init; }

    /// <summary>Outputs of previous relevant agents (agentCode -&gt; structured JSON).</summary>
    public required IReadOnlyDictionary<string, string> PreviousAgentOutputs { get; init; }

    public required FactorySettings FactorySettings { get; init; }

    public required string Model { get; init; }
    public required string InputVersion { get; init; }
    public required string IdempotencyKey { get; init; }
}

public sealed record AgentInputAsset
{
    public required Domain.Enums.AssetType AssetType { get; init; }
    public required string AssetCode { get; init; }
    public required string Version { get; init; }
    public string? DriveFileId { get; init; }
    public string? DriveUrl { get; init; }
    public required string ContentJson { get; init; }
}

/// <summary>The result of one agent execution after validation (never raw LLM text).</summary>
public sealed record AgentExecutionResult
{
    public required string AgentCode { get; init; }
    public required string ProjectCode { get; init; }

    /// <summary>COMPLETE, FAILED, NEEDS_REVIEW or HUMAN_REVIEW.</summary>
    public required string Status { get; init; }

    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    /// <summary>The structured GateRecommendation: ADVANCE_GATE | ROLLBACK | PAUSE | REJECT | HUMAN_REVIEW.</summary>
    public string? GateRecommendation { get; init; }

    public string Summary { get; init; } = string.Empty;

    /// <summary>Serialized structured output (what will be persisted and pushed to Drive).</summary>
    public required string OutputJson { get; init; }

    /// <summary>Registered request metadata for observability (not free-form prose).</summary>
    public AgentRunMetadata Metadata { get; init; } = AgentRunMetadata.Empty;
}

public sealed record AgentRunMetadata
{
    public static AgentRunMetadata Empty => new();
    public string? Model { get; init; }
    public string? OpenAiRequestId { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
}