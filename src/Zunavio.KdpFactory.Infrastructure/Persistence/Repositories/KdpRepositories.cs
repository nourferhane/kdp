using Microsoft.EntityFrameworkCore;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Domain.ValueObjects;

namespace Zunavio.KdpFactory.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(KdpDbContext db) : IProjectRepository
{
    public async Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct) =>
        await db.Projects.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(ct);

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Projects.Include(p => p.Assets).Include(p => p.Runs).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectCode == projectCode, ct);

    public async Task<Project?> GetByExternalIdAsync(string externalId, CancellationToken ct) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ExternalId == externalId, ct);

    public async Task AddAsync(Project project, CancellationToken ct) => await db.Projects.AddAsync(project, ct);

    public Task<int> GetProjectSequenceAsync(CancellationToken ct) =>
        db.Projects.AsNoTracking().CountAsync(ct);

    public async Task<IReadOnlyList<Project>> GetActiveAsync(CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Active)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Project>> WithPendingReviewsAsync(CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(p => p.Reviews.Any(r => r.Status == HumanReviewStatus.Pending))
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);
}

public sealed class AgentDefinitionRepository(KdpDbContext db) : IAgentDefinitionRepository
{
    public async Task<AgentDefinition?> GetByCodeAsync(string code, CancellationToken ct) =>
        await db.AgentDefinitions.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct);

    public async Task<AgentDefinition?> GetByAgentCodeAsync(AgentCode code, CancellationToken ct) =>
        await db.AgentDefinitions.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code.ToString(), ct);

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct) =>
        await db.AgentDefinitions.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);

    public async Task<AgentDefinition?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.AgentDefinitions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(AgentDefinition definition, CancellationToken ct) =>
        await db.AgentDefinitions.AddAsync(definition, ct);

    public async Task AddRangeAsync(IEnumerable<AgentDefinition> definitions, CancellationToken ct) =>
        await db.AgentDefinitions.AddRangeAsync(definitions, ct);
}

public sealed class AgentRunRepository(KdpDbContext db) : IAgentRunRepository
{
    public Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<AgentRun?> GetByCodeAsync(string runCode, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking().Include(r => r.AgentDefinition).FirstOrDefaultAsync(r => r.RunCode == runCode, ct);

    public async Task<IReadOnlyList<AgentRun>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.AgentRuns.AsNoTracking().Where(r => r.ProjectId == projectId).OrderBy(r => r.CreatedAt).ToListAsync(ct);

    public Task<AgentRun?> GetLatestForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.AgentDefinition!.Code == agentCode.ToString())
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<AgentRun?> GetLatestCompleteForAgentAsync(Guid projectId, AgentCode agentCode, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.AgentDefinition!.Code == agentCode.ToString()
                && r.Status == AgentRunStatus.Complete)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<bool> HasActiveDuplicateAsync(string idempotencyKey, CancellationToken ct) =>
        db.AgentRuns.AnyAsync(
            r => r.IdempotencyKey == idempotencyKey
                && (r.Status == AgentRunStatus.Running || r.Status == AgentRunStatus.Pending), ct);

    public Task<AgentRun?> GetLatestForKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.AgentRuns
            .Where(r => r.IdempotencyKey == idempotencyKey)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(AgentRun run, CancellationToken ct) => await db.AgentRuns.AddAsync(run, ct);

    public Task<int> GetRunSequenceAsync(Guid projectId, CancellationToken ct) =>
        db.AgentRuns.AsNoTracking().CountAsync(r => r.ProjectId == projectId, ct);
}

public sealed class AssetRepository(KdpDbContext db) : IAssetRepository
{
    public async Task<IReadOnlyList<Asset>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Assets.AsNoTracking().Where(a => a.ProjectId == projectId).OrderBy(a => a.AssetType).ThenBy(a => a.Version).ToListAsync(ct);

    public async Task<IReadOnlyList<Asset>> GetApprovedByTypeAsync(Guid projectId, AssetType type, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.AssetType == type && a.Status == AssetStatus.Approved)
            .OrderBy(a => a.Version)
            .ToListAsync(ct);
        return LatestPerType(assets);
    }

    public async Task<IReadOnlyList<Asset>> GetLatestApprovedForProjectAsync(Guid projectId, CancellationToken ct)
    {
        var assets = await db.Assets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.Status == AssetStatus.Approved)
            .OrderBy(a => a.AssetType).ThenBy(a => a.Version)
            .ToListAsync(ct);
        return LatestPerType(assets);
    }

    public Task<Asset?> GetLatestApprovedAsync(Guid projectId, AssetType type, CancellationToken ct) =>
        db.Assets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.AssetType == type && a.Status == AssetStatus.Approved)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync(ct);

    public Task<Asset?> GetLatestAsync(Guid projectId, AssetType type, CancellationToken ct) =>
        db.Assets.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.AssetType == type)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync(ct);

    public Task<Asset?> GetByCodeAsync(string assetCode, CancellationToken ct) =>
        db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.AssetCode == assetCode, ct);

    public Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task AddAsync(Asset asset, CancellationToken ct) => await db.Assets.AddAsync(asset, ct);

    public async Task<IReadOnlyList<Asset>> GetAllAsync(CancellationToken ct) =>
        await db.Assets.AsNoTracking().OrderBy(a => a.ProjectId).ThenBy(a => a.AssetType).ToListAsync(ct);

    private static IReadOnlyList<Asset> LatestPerType(IReadOnlyList<Asset> assets)
    {
        var best = new Dictionary<AssetType, Asset>();
        foreach (var asset in assets)
        {
            if (best.TryGetValue(asset.AssetType, out var current))
            {
                if (CompareVersions(asset.Version, current.Version) > 0)
                {
                    best[asset.AssetType] = asset;
                }
            }
            else
            {
                best[asset.AssetType] = asset;
            }
        }

        return best.Values
            .OrderBy(a => a.AssetType)
            .ToList();
    }

    private static int CompareVersions(string left, string right)
    {
        var a = VersionNumber.Parse(left);
        var b = VersionNumber.Parse(right);
        var major = a.Major.CompareTo(b.Major);
        return major != 0 ? major : a.Minor.CompareTo(b.Minor);
    }
}

public sealed class HumanReviewRepository(KdpDbContext db) : IHumanReviewRepository
{
    public Task<HumanReviewRequest?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.HumanReviewRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<HumanReviewRequest>> GetPendingForProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.HumanReviewRequests.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Status == HumanReviewStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HumanReviewRequest>> GetPendingAsync(CancellationToken ct) =>
        await db.HumanReviewRequests.AsNoTracking()
            .Where(r => r.Status == HumanReviewStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HumanReviewRequest>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.HumanReviewRequests.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);

    public async Task AddAsync(HumanReviewRequest request, CancellationToken ct) =>
        await db.HumanReviewRequests.AddAsync(request, ct);
}

public sealed class WorkflowEventRepository(KdpDbContext db) : IWorkflowEventRepository
{
    public async Task AddAsync(WorkflowEvent workflowEvent, CancellationToken ct) =>
        await db.WorkflowEvents.AddAsync(workflowEvent, ct);

    public async Task<IReadOnlyList<WorkflowEvent>> GetByProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.WorkflowEvents.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
}