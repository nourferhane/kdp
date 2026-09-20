using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>
/// A registry entry describing one agent. Google Drive owns the prompt text;
/// this table caches version + hash for snapshots and freshness checks.
/// </summary>
public class AgentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(512)]
    public string? PromptDriveFileId { get; set; }

    [MaxLength(255)]
    public string? PromptDriveUrl { get; set; }

    [MaxLength(32)]
    public string PromptVersion { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;

    /// <summary>Cached copy of the prompt fetched from Drive. Drive remains the authoring source.</summary>
    public string? PromptTextCache { get; set; }

    public DateTime? PromptLastSyncedAt { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AgentRun> Runs { get; set; } = [];

    public AgentCode ToAgentCode() => Enum.TryParse<AgentCode>(Code, true, out var code) ? code : throw new InvalidOperationException($"Unknown agent code '{Code}'.");
}