using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.BackgroundJobs;

/// <summary>
/// The durable worker. A single instance processes one job at a time, claiming
/// rows with FOR UPDATE SKIP LOCKED through the repository (section 19).
/// Running in a single process (web + worker) makes processing deterministic;
/// deploying the worker separately is possible by disabling it in the web app
/// (RUN_BACKGROUND_WORKER=false) and running it as its own process.
/// </summary>
public sealed class BackgroundJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WorkerOptions> _options;
    private readonly ILogger<BackgroundJobWorker> _logger;

    public BackgroundJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WorkerOptions> options,
        ILogger<BackgroundJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job worker started (poll {Poll}s, lease {Lease}s, max {Max} attempts).",
            _options.CurrentValue.PollIntervalSeconds, _options.CurrentValue.LeaseSeconds, _options.CurrentValue.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;
            try
            {
                processedAny = await ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker poll iteration failed.");
            }

            if (!processedAny)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.PollIntervalSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Background job worker stopped.");
    }

    private async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobRepository>();

        // Recover jobs whose lease expired before claiming new work.
        await jobs.RequeueStaleAsync(_options.CurrentValue.LeaseSeconds, ct);

        var job = await jobs.ClaimNextAsync(ct);
        if (job is null)
        {
            return false;
        }

        var dispatcher = scope.ServiceProvider.GetRequiredService<IJobDispatcher>();
        _logger.LogInformation("Claimed job {JobId} ({JobType}, attempt {Attempt}).", job.Id, job.JobType, job.Attempts + 1);

        try
        {
            await dispatcher.ExecuteAsync(job, ct);
            await jobs.MarkCompletedAsync(job.Id, ct);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobs.MarkFailedAsync(job.Id, "Cancelled at shutdown.", _options.CurrentValue.MaxAttempts, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} ({JobType}) failed.", job.Id, job.JobType);
            await jobs.MarkFailedAsync(job.Id, Truncate(ex.Message, 3900), _options.CurrentValue.MaxAttempts, ct);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }

        return true;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}