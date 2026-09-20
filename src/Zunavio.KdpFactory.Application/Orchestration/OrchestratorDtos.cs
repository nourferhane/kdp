namespace Zunavio.KdpFactory.Application.Orchestration;

public sealed record CreateProjectRequest
{
    public required string WorkingTitle { get; init; }
    public string Marketplace { get; init; } = "Amazon.com";
    public string Language { get; init; } = "English";
    public string TargetAge { get; init; } = string.Empty;
    public string BookType { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string? ProjectCode { get; init; }
    public string? DriveFolderId { get; init; }
    public string? ExternalId { get; init; }
}

public sealed record OrchestratorOperationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public Guid? JobId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ReviewId { get; init; }

    public static OrchestratorOperationResult Ok(string message, Guid? jobId = null, Guid? projectId = null, Guid? reviewId = null) =>
        new() { Success = true, Message = message, JobId = jobId, ProjectId = projectId, ReviewId = reviewId };

    public static OrchestratorOperationResult Fail(string error) => new() { Success = false, Error = error };
}