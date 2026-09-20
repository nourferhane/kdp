using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>A KDP book production project.</summary>
public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(16)]
    public string ProjectCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string WorkingTitle { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? FinalTitle { get; set; }

    [MaxLength(64)]
    public string Marketplace { get; set; } = "Amazon.com";

    [MaxLength(32)]
    public string Language { get; set; } = "English";

    [MaxLength(32)]
    public string TargetAge { get; set; } = string.Empty;

    [MaxLength(64)]
    public string BookType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Season { get; set; } = string.Empty;

    public ProjectGate CurrentGate { get; set; } = ProjectGate.Idea;
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public int? MarketScore { get; set; }

    [MaxLength(16)]
    public string CurrentManuscriptVersion { get; set; } = string.Empty;

    [MaxLength(16)]
    public string CurrentVisualBibleVersion { get; set; } = string.Empty;

    [MaxLength(16)]
    public string CurrentProductionVersion { get; set; } = string.Empty;

    /// <summary>Serialized QaResult (as returned by the QA agent).</summary>
    public string? QaResult { get; set; }

    [MaxLength(64)]
    public string NextAction { get; set; } = string.Empty;

    /// <summary>Serialized selected concept (JSON), chosen via ConceptSelection human review.</summary>
    public string? SelectedConceptJson { get; set; }

    [MaxLength(255)]
    public string? DriveFolderId { get; set; }

    [MaxLength(255)]
    public string? DriveFolderUrl { get; set; }

    [MaxLength(128)]
    public string? ExternalId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency token. Incremented by a BEFORE UPDATE trigger; used as EF concurrency token.</summary>
    public long RowVersion { get; set; }

    public ICollection<AgentRun> Runs { get; set; } = [];
    public ICollection<Asset> Assets { get; set; } = [];
    public ICollection<HumanReviewRequest> Reviews { get; set; } = [];
    public ICollection<WorkflowEvent> Events { get; set; } = [];
}