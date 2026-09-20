using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>Registry of durable job types enqueued into the background queue.</summary>
public static class JobTypes
{
    public const string RunAgent = nameof(RunAgent);
    public const string ImportControlCenter = nameof(ImportControlCenter);
    public const string SyncProject = nameof(SyncProject);
    public const string RefreshAgentPrompts = nameof(RefreshAgentPrompts);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "job", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(RunAgentJobPayload), JobTypes.RunAgent)]
[JsonDerivedType(typeof(ImportControlCenterJobPayload), JobTypes.ImportControlCenter)]
[JsonDerivedType(typeof(SyncProjectJobPayload), JobTypes.SyncProject)]
[JsonDerivedType(typeof(RefreshAgentPromptsJobPayload), JobTypes.RefreshAgentPrompts)]
public abstract class BackgroundJobPayload;

public sealed class RunAgentJobPayload : BackgroundJobPayload
{
    public Guid ProjectId { get; set; }
    public string? AgentCode { get; set; }
}

public sealed class ImportControlCenterJobPayload : BackgroundJobPayload;

public sealed class SyncProjectJobPayload : BackgroundJobPayload
{
    public Guid ProjectId { get; set; }
}

public sealed class RefreshAgentPromptsJobPayload : BackgroundJobPayload;

/// <summary>Codec used by the job manager and the worker to serialize payloads.</summary>
public static class BackgroundJobPayloadCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = false,
    };

    static BackgroundJobPayloadCodec()
    {
        // Polymorphic serialization requires the attribute-based contract shown above.
    }

    public static string Serialize(BackgroundJobPayload payload)
    {
        var type = payload.GetType().Name switch
        {
            nameof(RunAgentJobPayload) => JobTypes.RunAgent,
            nameof(ImportControlCenterJobPayload) => JobTypes.ImportControlCenter,
            nameof(SyncProjectJobPayload) => JobTypes.SyncProject,
            nameof(RefreshAgentPromptsJobPayload) => JobTypes.RefreshAgentPrompts,
            _ => throw new InvalidOperationException($"Unknown payload type {payload.GetType().Name}."),
        };

        using var result = JsonSerializer.SerializeToDocument(payload, payload.GetType());
        return JsonSerializer.Serialize(new JsonObjectWithDiscriminator { job = type, payload = result.RootElement.Clone() });
    }

    public static (string JobType, BackgroundJobPayload Payload) Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var jobType = root.TryGetProperty("job", out var job) ? job.GetString() ?? string.Empty : string.Empty;
        var payload = root.TryGetProperty("payload", out var p) ? p : default;
        return jobType switch
        {
            JobTypes.RunAgent => (JobTypes.RunAgent, payload.Deserialize<RunAgentJobPayload>(Options) ?? new RunAgentJobPayload()),
            JobTypes.ImportControlCenter => (JobTypes.ImportControlCenter, payload.Deserialize<ImportControlCenterJobPayload>(Options) ?? new ImportControlCenterJobPayload()),
            JobTypes.SyncProject => (JobTypes.SyncProject, payload.Deserialize<SyncProjectJobPayload>(Options) ?? new SyncProjectJobPayload()),
            JobTypes.RefreshAgentPrompts => (JobTypes.RefreshAgentPrompts, payload.Deserialize<RefreshAgentPromptsJobPayload>(Options) ?? new RefreshAgentPromptsJobPayload()),
            _ => throw new InvalidOperationException($"Unknown job type '{jobType}'."),
        };
    }

    private sealed class JsonObjectWithDiscriminator
    {
        public string job { get; set; } = string.Empty;
        public JsonElement payload { get; set; }
    }
}

/// <summary>
/// Enqueues work for the durable worker and protects against duplicate jobs
/// (section 18/19).
/// </summary>
public interface IBackgroundJobManager
{
    Task<Guid> EnqueueRunAgentAsync(Guid projectId, string? agentCode, CancellationToken ct);
    Task<Guid> EnqueueImportControlCenterAsync(CancellationToken ct);
    Task<Guid> EnqueueSyncProjectAsync(Guid projectId, CancellationToken ct);
    Task<bool> HasBlockingReviewAsync(Guid projectId, CancellationToken ct);
}