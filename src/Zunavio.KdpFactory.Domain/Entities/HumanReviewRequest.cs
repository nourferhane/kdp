using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>
/// A human approval gate. Workflow must not continue while a blocking
/// request is Pending.
/// </summary>
public class HumanReviewRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Guid? AgentRunId { get; set; }

    public HumanReviewType ReviewType { get; set; }

    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>Optional structured payload, e.g. the concept candidates JSON.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>When a review carries options (e.g. concepts) the human may select the winning one.</summary>
    [MaxLength(2048)]
    public string? ResolutionPayloadJson { get; set; }

    public HumanReviewStatus Status { get; set; } = HumanReviewStatus.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    [MaxLength(4000)]
    public string? ResolutionComment { get; set; }

    public Project? Project { get; set; }
    public AgentRun? AgentRun { get; set; }
}