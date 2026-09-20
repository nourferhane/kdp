using System.ComponentModel.DataAnnotations;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Domain.Entities;

/// <summary>
/// A durable, database-backed job used to decouple long AI executions from
/// HTTP requests. Claimed with FOR UPDATE SKIP LOCKED by the worker.
/// </summary>
public class BackgroundJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(32)]
    public string JobType { get; set; } = string.Empty;

    public string? PayloadJson { get; set; }

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Pending;

    public int Attempts { get; set; }

    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;

    public DateTime? LockedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(4000)]
    public string? LastError { get; set; }

    /// <summary>Unique key protecting against duplicate enqueues.</summary>
    [MaxLength(255)]
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}