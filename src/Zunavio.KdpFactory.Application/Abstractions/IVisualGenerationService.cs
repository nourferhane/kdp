namespace Zunavio.KdpFactory.Application.Abstractions;

public sealed record VisualGenerationRequest
{
    public required string ProjectCode { get; init; }
    public required int PageNumber { get; init; }
    public required string Prompt { get; init; }
    public string? Size { get; init; }
    public string? Quality { get; init; }
}

public sealed record VisualGenerationResult
{
    public required bool Success { get; init; }
    public required bool Verified { get; init; }
    public required string ProjectCode { get; init; }
    public required int PageNumber { get; init; }
    public required string AssetCode { get; init; }
    public required string DriveFileId { get; init; }
    public required string DriveUrl { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
    public required string Model { get; init; }
    public string? RevisedPrompt { get; init; }
    public bool Idempotent { get; init; }
}

public interface IVisualGenerationService
{
    Task<VisualGenerationResult> GenerateAndPersistAsync(VisualGenerationRequest request, CancellationToken ct);
}
