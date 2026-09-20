using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Projects;

public record ProjectSummaryDto
{
    public Guid Id { get; init; }
    public string ProjectCode { get; init; } = string.Empty;
    public string WorkingTitle { get; init; } = string.Empty;
    public string? FinalTitle { get; init; }
    public ProjectGate CurrentGate { get; init; }
    public ProjectStatus Status { get; init; }
    public int? MarketScore { get; init; }
    public string NextAction { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; }
    public string Marketplace { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string TargetAge { get; init; } = string.Empty;
    public string BookType { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int PendingReviews { get; init; }
    public bool HasRunningJobs { get; init; }
}

public sealed record ProjectDetailDto : ProjectSummaryDto
{
    public string DriveFolderUrl { get; init; } = string.Empty;
    public string CurrentManuscriptVersion { get; init; } = string.Empty;
    public string CurrentVisualBibleVersion { get; init; } = string.Empty;
    public string CurrentProductionVersion { get; init; } = string.Empty;
    public string? QaResult { get; init; }
}

public sealed record AgentDefinitionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? PromptDriveFileId { get; init; }
    public string PromptVersion { get; init; } = string.Empty;
    public string PromptHash { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public DateTime? PromptLastSyncedAt { get; init; }
}

public sealed record ReviewDto
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? AgentRunId { get; init; }
    public string ProjectCode { get; init; } = string.Empty;
    public HumanReviewType ReviewType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? PayloadJson { get; init; }
    public HumanReviewStatus Status { get; init; }
    public DateTime RequestedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolutionComment { get; init; }
}

public sealed record JobDto
{
    public Guid Id { get; init; }
    public string JobType { get; init; } = string.Empty;
    public BackgroundJobStatus Status { get; init; }
    public int Attempts { get; init; }
    public DateTime AvailableAt { get; init; }
    public DateTime? LockedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? LastError { get; init; }
    public Guid? ProjectId { get; init; }
}