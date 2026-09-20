using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Projects;

namespace Zunavio.KdpFactory.Application.Services;

public sealed class AgentDefinitionQueryService : IAgentDefinitionQueryService
{
    private readonly IUnitOfWork _db;

    public AgentDefinitionQueryService(IUnitOfWork db) => _db = db;

    public async Task<IReadOnlyList<AgentDefinitionDto>> GetAllAsync(CancellationToken ct)
    {
        var agents = await _db.Agents.GetAllAsync(ct);
        return agents
            .OrderBy(a => a.Code)
            .Select(a => new AgentDefinitionDto
            {
                Id = a.Id,
                Code = a.Code,
                Name = a.Name,
                Description = a.Description ?? string.Empty,
                PromptDriveFileId = a.PromptDriveFileId,
                PromptVersion = a.PromptVersion,
                PromptHash = a.PromptHash,
                Enabled = a.Enabled,
                PromptLastSyncedAt = a.PromptLastSyncedAt,
            })
            .ToList();
    }
}

public sealed class AgentPromptRefreshService : IAgentPromptRefreshService
{
    private readonly IUnitOfWork _db;
    private readonly IAgentPromptLoader _loader;

    public AgentPromptRefreshService(IUnitOfWork db, IAgentPromptLoader loader)
    {
        _db = db;
        _loader = loader;
    }

    public async Task<IReadOnlyList<Domain.Entities.AgentDefinition>> GetAllAsync(CancellationToken ct) =>
        await _db.Agents.GetAllAsync(ct);

    public async Task<PromptSnapshot> RefreshAsync(Guid agentDefinitionId, CancellationToken ct)
    {
        var definition = await _db.Agents.GetByIdAsync(agentDefinitionId, ct)
            ?? throw new KeyNotFoundException($"Agent definition '{agentDefinitionId}' not found.");

        var snapshot = await _loader.RefreshAsync(definition, ct);
        await _db.SaveChangesAsync(ct);
        return snapshot;
    }
}

public sealed class ReviewQueryService : IReviewQueryService
{
    private readonly IUnitOfWork _db;

    public ReviewQueryService(IUnitOfWork db) => _db = db;

    public async Task<IReadOnlyList<ReviewDto>> GetPendingAsync(CancellationToken ct)
    {
        var reviews = await _db.Reviews.GetPendingAsync(ct);
        var result = new List<ReviewDto>(reviews.Count);
        foreach (var review in reviews)
        {
            var project = review.Project ?? await _db.Projects.GetByIdAsync(review.ProjectId, ct);
            result.Add(ToDto(review, project?.ProjectCode ?? string.Empty));
        }
        return result;
    }

    public async Task<IReadOnlyList<ReviewDto>> GetByProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.GetByIdAsync(projectId, ct);
        var reviews = await _db.Reviews.GetByProjectAsync(projectId, ct);
        return reviews.Select(r => ToDto(r, project?.ProjectCode ?? string.Empty))
            .OrderByDescending(r => r.RequestedAt)
            .ToList();
    }

    private static ReviewDto ToDto(Domain.Entities.HumanReviewRequest r, string projectCode) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        AgentRunId = r.AgentRunId,
        ProjectCode = projectCode,
        ReviewType = r.ReviewType,
        Title = r.Title,
        Description = r.Description ?? string.Empty,
        PayloadJson = r.PayloadJson,
        Status = r.Status,
        RequestedAt = r.RequestedAt,
        ResolvedAt = r.ResolvedAt,
        ResolutionComment = r.ResolutionComment,
    };
}

public sealed class JobQueryService : IJobQueryService
{
    private readonly IUnitOfWork _db;

    public JobQueryService(IUnitOfWork db) => _db = db;

    public async Task<IReadOnlyList<JobDto>> GetRecentAsync(int take, CancellationToken ct)
    {
        var jobs = await _db.Jobs.GetRecentAsync(take, ct);
        return jobs.Select(j => new JobDto
        {
            Id = j.Id,
            JobType = j.JobType,
            Status = j.Status,
            Attempts = j.Attempts,
            AvailableAt = j.AvailableAt,
            LockedAt = j.LockedAt,
            CompletedAt = j.CompletedAt,
            LastError = j.LastError,
            ProjectId = TryGetProjectId(j.PayloadJson),
        }).ToList();
    }

    private static Guid? TryGetProjectId(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("payload", out var payload) && payload.TryGetProperty("projectId", out var projectId))
                return Guid.TryParse(projectId.GetString(), out var id) ? id : null;
        }
        catch (System.Text.Json.JsonException)
        {
            // ignore
        }
        return null;
    }
}