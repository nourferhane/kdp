using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Services;

/// <summary>Manages the durable queue while protecting against duplicate enqueues.</summary>
public sealed class BackgroundJobManager : IBackgroundJobManager
{
    private readonly IUnitOfWork _db;
    private readonly IBackgroundJobRepository _jobs;
    private readonly IHumanReviewRepository _reviews;

    public BackgroundJobManager(IUnitOfWork db, IBackgroundJobRepository jobs, IHumanReviewRepository reviews)
    {
        _db = db;
        _jobs = jobs;
        _reviews = reviews;
    }

    public async Task<Guid> EnqueueRunAgentAsync(Guid projectId, string? agentCode, CancellationToken ct)
    {
        var idempotencyKey = $"{projectId}|{JobTypes.RunAgent}|{agentCode ?? "*"}";
        if (await _jobs.ExistsActiveByIdempotencyKeyAsync(idempotencyKey, ct))
            return Guid.Empty;

        var payload = new RunAgentJobPayload { ProjectId = projectId, AgentCode = agentCode };
        return await AddAsync(JobTypes.RunAgent, BackgroundJobPayloadCodec.Serialize(payload), idempotencyKey, ct);
    }

    public Task<Guid> EnqueueImportControlCenterAsync(CancellationToken ct) =>
        AddAsync(JobTypes.ImportControlCenter, BackgroundJobPayloadCodec.Serialize(new ImportControlCenterJobPayload()), null, ct);

    public async Task<Guid> EnqueueSyncProjectAsync(Guid projectId, CancellationToken ct)
    {
        var idempotencyKey = $"{projectId}|{JobTypes.SyncProject}";
        if (await _jobs.ExistsActiveByIdempotencyKeyAsync(idempotencyKey, ct))
            return Guid.Empty;

        var payload = new SyncProjectJobPayload { ProjectId = projectId };
        return await AddAsync(JobTypes.SyncProject, BackgroundJobPayloadCodec.Serialize(payload), idempotencyKey, ct);
    }

    public async Task<bool> HasBlockingReviewAsync(Guid projectId, CancellationToken ct) =>
        (await _reviews.GetPendingForProjectAsync(projectId, ct)).Count > 0;

    private async Task<Guid> AddAsync(string jobType, string payloadJson, string? idempotencyKey, CancellationToken ct)
    {
        var job = new BackgroundJob
        {
            JobType = jobType,
            PayloadJson = payloadJson,
            Status = BackgroundJobStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTime.UtcNow,
            AvailableAt = DateTime.UtcNow,
        };

        await _jobs.AddAsync(job, ct);
        await _db.SaveChangesAsync(ct);
        return job.Id;
    }
}