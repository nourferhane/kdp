using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>A single immutable execution of an agent against a project.</summary>
public class AgentRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(32)]
    public string RunCode { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }
    public Guid AgentDefinitionId { get; set; }

    /// <summary>Version of the inputs this run was assembled from (e.g. the input asset version).</summary>
    [MaxLength(32)]
    public string InputVersion { get; set; } = string.Empty;

    /// <summary>Version assigned to the output produced by this run.</summary>
    [MaxLength(32)]
    public string OutputVersion { get; set; } = string.Empty;

    public AgentRunStatus Status { get; set; } = AgentRunStatus.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Immutable copy of the prompt used. Guarantees reproducibility.</summary>
    public string PromptSnapshot { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? PromptVersion { get; set; }

    [MaxLength(64)]
    public string? PromptHash { get; set; }

    /// <summary>Serialized JSON inputs provided to the agent.</summary>
    public string? InputSnapshotJson { get; set; }

    /// <summary>Serialized structured JSON output produced by the agent.</summary>
    public string? OutputJson { get; set; }

    [MaxLength(4000)]
    public string? Summary { get; set; }

    /// <summary>Serialized list of blocking issues.</summary>
    public string? BlockingIssuesJson { get; set; }

    /// <summary>Structured gate recommendation made by the agent or the orchestrator decision.</summary>
    [MaxLength(64)]
    public string? GateRecommendation { get; set; }

    [MaxLength(128)]
    public string? Model { get; set; }

    [MaxLength(128)]
    public string? OpenAiRequestId { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCost { get; set; }

    [MaxLength(4000)]
    public string? ErrorMessage { get; set; }

    public int RetryCount { get; set; }

    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
    public AgentDefinition? AgentDefinition { get; set; }
    public ICollection<Asset> Assets { get; set; } = [];
}