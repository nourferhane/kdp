using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Projects;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Services;

public sealed class ProjectQueryService : IProjectQueryService
{
    private readonly IUnitOfWork _db;

    public ProjectQueryService(IUnitOfWork db) => _db = db;

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetAllAsync(CancellationToken ct)
    {
        var projects = await _db.Projects.GetAllAsync(ct);
        var result = new List<ProjectSummaryDto>(projects.Count);

        foreach (var project in projects)
        {
            var pending = await _db.Reviews.GetPendingForProjectAsync(project.Id, ct);
            var hasJobs = (await _db.Runs.GetByProjectAsync(project.Id, ct))
                .Any(r => r.Status is AgentRunStatus.Running or AgentRunStatus.Pending);
            result.Add(ToSummary(project, pending, hasJobs));
        }

        return result.OrderByDescending(p => p.UpdatedAt).ToList();
    }

    public async Task<ProjectDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(id, ct);
        if (project is null) return null;

        var pending = await _db.Reviews.GetPendingForProjectAsync(project.Id, ct);
        var runs = await _db.Runs.GetByProjectAsync(project.Id, ct);
        var hasJobs = runs.Any(r => r.Status is AgentRunStatus.Running or AgentRunStatus.Pending);
        var agents = (await _db.Agents.GetAllAsync(ct)).ToDictionary(a => a.Id, a => a);
        var assets = await _db.Assets.GetByProjectAsync(project.Id, ct);

        var summary = ToSummary(project, pending, hasJobs);
        return new ProjectDetailDto
        {
            Id = summary.Id,
            ProjectCode = summary.ProjectCode,
            WorkingTitle = summary.WorkingTitle,
            FinalTitle = summary.FinalTitle,
            CurrentGate = summary.CurrentGate,
            Status = summary.Status,
            MarketScore = summary.MarketScore,
            NextAction = summary.NextAction,
            UpdatedAt = summary.UpdatedAt,
            Marketplace = summary.Marketplace,
            Language = summary.Language,
            TargetAge = summary.TargetAge,
            BookType = summary.BookType,
            Season = summary.Season,
            PendingReviews = summary.PendingReviews,
            HasRunningJobs = summary.HasRunningJobs,
            BlockedReason = summary.BlockedReason,
            DriveFolderUrl = project.DriveFolderUrl ?? string.Empty,
            CurrentManuscriptVersion = project.CurrentManuscriptVersion,
            CurrentVisualBibleVersion = project.CurrentVisualBibleVersion,
            CurrentProductionVersion = project.CurrentProductionVersion,
            QaResult = project.QaResult,
            AgentRuns = runs
                .OrderByDescending(r => r.CompletedAt ?? r.CreatedAt)
                .Select(r => ToRun(r, agents.GetValueOrDefault(r.AgentDefinitionId)))
                .ToList(),
            Assets = assets
                .OrderByDescending(a => a.CreatedAt)
                .Select(ToAsset)
                .ToList(),
        };
    }

    private static ProjectSummaryDto ToSummary(
        Domain.Entities.Project p,
        IReadOnlyList<Domain.Entities.HumanReviewRequest> pendingReviews,
        bool hasJobs) => new()
    {
        Id = p.Id,
        ProjectCode = p.ProjectCode,
        WorkingTitle = p.WorkingTitle,
        FinalTitle = p.FinalTitle,
        CurrentGate = p.CurrentGate,
        Status = p.Status,
        MarketScore = p.MarketScore,
        NextAction = p.NextAction,
        UpdatedAt = p.UpdatedAt,
        Marketplace = p.Marketplace,
        Language = p.Language,
        TargetAge = p.TargetAge,
        BookType = p.BookType,
        Season = p.Season,
        PendingReviews = pendingReviews.Count,
        HasRunningJobs = hasJobs,
        BlockedReason = ComputeBlockedReason(p, pendingReviews),
    };

    private static string? ComputeBlockedReason(
        Domain.Entities.Project p,
        IReadOnlyList<Domain.Entities.HumanReviewRequest> pendingReviews)
    {
        if (pendingReviews.Count > 0)
        {
            var titles = pendingReviews.Take(2).Select(r => r.Title).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            var suffix = pendingReviews.Count > 2 ? $" (+{pendingReviews.Count - 2} more)" : string.Empty;
            return titles.Count == 0
                ? $"Waiting for human review ({pendingReviews.Count})"
                : $"Waiting for human review: {string.Join("; ", titles)}{suffix}";
        }

        return p.Status switch
        {
            ProjectStatus.Paused => "Paused (on hold)",
            ProjectStatus.Rejected => "Rejected",
            _ => null,
        };
    }

    private static AgentRunDto ToRun(
        Domain.Entities.AgentRun r,
        Domain.Entities.AgentDefinition? agent) => new()
    {
        Id = r.Id,
        RunCode = r.RunCode,
        AgentCode = agent?.Code ?? string.Empty,
        AgentName = agent?.Name ?? "Agent",
        Status = r.Status,
        GateRecommendation = r.GateRecommendation,
        Model = r.Model,
        Summary = r.Summary,
        ErrorMessage = r.ErrorMessage,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        CreatedAt = r.CreatedAt,
    };

    private static AssetSummaryDto ToAsset(Domain.Entities.Asset a) => new()
    {
        Id = a.Id,
        AssetCode = a.AssetCode,
        AssetType = a.AssetType,
        Version = a.Version,
        Status = a.Status,
        QaStatus = a.QaStatus,
        DriveUrl = a.DriveUrl,
        CreatedAt = a.CreatedAt,
    };
}