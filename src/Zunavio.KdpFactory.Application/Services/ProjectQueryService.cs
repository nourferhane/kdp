using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Projects;

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
            var hasJobs = (await _db.Runs.GetByProjectAsync(project.Id, ct)).Any(r => r.Status is Domain.Enums.AgentRunStatus.Running or Domain.Enums.AgentRunStatus.Pending);
            result.Add(ToSummary(project, pending.Count, hasJobs));
        }

        return result.OrderByDescending(p => p.UpdatedAt).ToList();
    }

    public async Task<ProjectDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(id, ct);
        if (project is null) return null;

        var pending = await _db.Reviews.GetPendingForProjectAsync(project.Id, ct);
        var hasJobs = (await _db.Runs.GetByProjectAsync(project.Id, ct)).Any(r => r.Status is Domain.Enums.AgentRunStatus.Running or Domain.Enums.AgentRunStatus.Pending);

        var summary = ToSummary(project, pending.Count, hasJobs);
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
            DriveFolderUrl = project.DriveFolderUrl ?? string.Empty,
            CurrentManuscriptVersion = project.CurrentManuscriptVersion,
            CurrentVisualBibleVersion = project.CurrentVisualBibleVersion,
            CurrentProductionVersion = project.CurrentProductionVersion,
            QaResult = project.QaResult,
        };
    }

    private static ProjectSummaryDto ToSummary(Domain.Entities.Project p, int pendingReviews, bool hasJobs) => new()
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
        PendingReviews = pendingReviews,
        HasRunningJobs = hasJobs,
    };
}