using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Infrastructure.Persistence.Repositories;

public sealed class BackgroundJobRepository(KdpDbContext db) : IBackgroundJobRepository
{
    public async Task AddAsync(BackgroundJob job, CancellationToken ct) =>
        await db.BackgroundJobs.AddAsync(job, ct);

    public Task<BackgroundJob?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<bool> ExistsActiveByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        db.BackgroundJobs.AnyAsync(
            j => j.IdempotencyKey == idempotencyKey
                && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running), ct);

    public async Task<BackgroundJob?> ClaimNextAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
             UPDATE background_jobs
             SET "Status" = {(int)BackgroundJobStatus.Running}, "LockedAt" = now(), "LastError" = NULL
             WHERE "Id" = (
                 SELECT j."Id" FROM background_jobs AS j
                 WHERE j."Status" = {(int)BackgroundJobStatus.Pending}
                   AND j."AvailableAt" <= now()
                 ORDER BY j."CreatedAt"
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
             )
             RETURNING "Id", "JobType", "PayloadJson", "Status", "Attempts", "AvailableAt", "LockedAt", "CompletedAt", "LastError", "IdempotencyKey", "CreatedAt"
             """;
        try
        {
            await using var reader = (NpgsqlDataReader)await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return Map(reader);
            }
        }
        catch (NpgsqlException)
        {
            // Transient claim failure; job stays Pending and will be retried next poll.
            return null;
        }

        return null;
    }

    public async Task<int> RequeueStaleAsync(int leaseSeconds, CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(Math.Max(10, leaseSeconds));
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
             UPDATE background_jobs
             SET "Status" = {(int)BackgroundJobStatus.Pending},
                 "AvailableAt" = now(),
                 "LockedAt" = NULL
             WHERE "Status" = {(int)BackgroundJobStatus.Running}
               AND "LockedAt" IS NOT NULL
               AND "LockedAt" < now() - {period.TotalSeconds} * interval '1 second'
             """;
        try
        {
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException)
        {
            return 0;
        }
    }

    public async Task MarkCompletedAsync(Guid id, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FindAsync([id], ct);
        if (job is null) return;
        job.Status = BackgroundJobStatus.Complete;
        job.LockedAt = null;
        job.CompletedAt = DateTime.UtcNow;
    }

    public async Task MarkFailedAsync(Guid id, string error, int maxAttempts, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FindAsync([id], ct);
        if (job is null) return;

        job.Attempts++;
        job.LastError = error;
        job.LockedAt = null;

        if (job.Attempts >= Math.Max(1, maxAttempts))
        {
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            job.Status = BackgroundJobStatus.Pending;
            job.AvailableAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, job.Attempts));
        }
    }

    public async Task MarkCancelledAsync(Guid id, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FindAsync([id], ct);
        if (job is null) return;
        job.Status = BackgroundJobStatus.Cancelled;
        job.LockedAt = null;
        job.CompletedAt = DateTime.UtcNow;
    }

    public async Task<IReadOnlyList<BackgroundJob>> GetRecentAsync(int take, CancellationToken ct) =>
        await db.BackgroundJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    private static BackgroundJob Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        JobType = reader.GetString(1),
        PayloadJson = reader.IsDBNull(2) ? null : reader.GetString(2),
        Status = (BackgroundJobStatus)reader.GetInt32(3),
        Attempts = reader.GetInt32(4),
        AvailableAt = reader.GetFieldValue<DateTime>(5),
        LockedAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTime>(6),
        CompletedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTime>(7),
        LastError = reader.IsDBNull(8) ? null : reader.GetString(8),
        IdempotencyKey = reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedAt = reader.GetFieldValue<DateTime>(10),
    };
}