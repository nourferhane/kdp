using Microsoft.Extensions.Logging;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Application.Orchestration;
using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Infrastructure.BackgroundJobs;

/// <summary>
/// Maps a claimed <see cref="BackgroundJob"/> to its engine operation. All
/// exceptions propagate to the worker, which decides retry vs. failure.
/// </summary>
public interface IJobDispatcher
{
    Task ExecuteAsync(BackgroundJob job, CancellationToken ct);
    IReadOnlyList<string> SupportedJobTypes { get; }
}

public sealed class JobDispatcher(IOrchestrationEngine engine, ILogger<JobDispatcher> logger) : IJobDispatcher
{
    public IReadOnlyList<string> SupportedJobTypes =>
        [JobTypes.RunAgent, JobTypes.ImportControlCenter, JobTypes.SyncProject, JobTypes.RefreshAgentPrompts];

    public async Task ExecuteAsync(BackgroundJob job, CancellationToken ct)
    {
        var (jobType, payload) = BackgroundJobPayloadCodec.Deserialize(job.PayloadJson ?? string.Empty);
        switch (jobType)
        {
            case JobTypes.RunAgent:
            {
                var run = (RunAgentJobPayload)payload;
                await engine.ExecuteRunAgentAsync(run, ct);
                break;
            }

            case JobTypes.ImportControlCenter:
                await engine.ExecuteImportControlCenterAsync((ImportControlCenterJobPayload)payload, ct);
                break;

            case JobTypes.SyncProject:
                await engine.ExecuteSyncProjectAsync((SyncProjectJobPayload)payload, ct);
                break;

            case JobTypes.RefreshAgentPrompts:
                await engine.ExecuteRefreshAgentPromptsAsync((RefreshAgentPromptsJobPayload)payload, ct);
                break;

            default:
                throw new InvalidOperationException($"Unsupported job type '{jobType}' for job {job.Id}.");
        }

        logger.LogDebug("Job {JobId} ({JobType}) executed.", job.Id, jobType);
    }
}