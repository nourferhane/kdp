using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>A produced artifact (report, manuscript, bible, pdf...).</summary>
public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(32)]
    public string AssetCode { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }

    public AssetType AssetType { get; set; }

    [MaxLength(16)]
    public string Version { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? DriveFileId { get; set; }

    [MaxLength(2048)]
    public string? DriveUrl { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Draft;

    public Guid? CreatedByAgentRunId { get; set; }

    public AssetQaStatus QaStatus { get; set; } = AssetQaStatus.NotChecked;

    /// <summary>Cached structured content produced by the creating run. Drive remains the authoring/display copy; this guarantees the engine works when Drive is unreachable.</summary>
    public string? ContentJson { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
    public AgentRun? CreatedByRun { get; set; }
}