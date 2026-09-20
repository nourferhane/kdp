using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>Append-only timeline entry for a project.</summary>
public class WorkflowEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }
    public Guid? AgentRunId { get; set; }
    public Guid? HumanReviewRequestId { get; set; }

    public WorkflowEventType Type { get; set; }

    /// <summary>Previously active gate, when a gate transition happened.</summary>
    public ProjectGate? OldGate { get; set; }

    /// <summary>Gate after a transition.</summary>
    public ProjectGate? NewGate { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}