using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Abstractions;

/// <summary>
/// A saved artifact reference produced by a run and stored in Google Drive.
/// </summary>
public sealed record ArtifactSaveResult
{
    public required string DriveFileId { get; init; }
    public required string DriveUrl { get; init; }
    public required string FolderId { get; init; }
    public required string ProjectFolderId { get; init; }
}

/// <summary>
/// Abstraction over Google Drive used to create folders, read Google Docs,
/// write Google Docs and resolve the correct per-project subfolder for an
/// asset type (section 16).
/// </summary>
public interface IArtifactStorage
{
    Task<string> EnsureRootStructureAsync(CancellationToken ct);
    Task<ProjectFolderRef> EnsureProjectFoldersAsync(string projectCode, CancellationToken ct);

    Task<DocumentRead> ReadDocumentAsync(string fileId, CancellationToken ct);

    /// <summary>Creates a Google Doc in the folder with the given textual content.</summary>
    Task<DocumentWrite> CreateDocumentAsync(string folderId, string title, string markdownContent, CancellationToken ct);

    /// <summary>Creates a folder under parentFolderId.</summary>
    Task<FolderRef> CreateFolderAsync(string parentFolderId, string name, CancellationToken ct);

    /// <summary>Finds a folder by name directly under root.</summary>
    Task<string?> FindFolderIdAsync(string rootFolderId, string name, CancellationToken ct);

    /// <summary>Builds the artifact filename: &lt;ProjectCode&gt;_&lt;AssetType&gt;_v&lt;Version&gt;.</summary>
    string BuildArtifactName(string projectCode, AssetType assetType, string version);

    /// <summary>Resolves the project subfolder name for an asset type (01_RESEARCH ... 08_QA).</summary>
    string FolderNameFor(AssetType assetType, string projectCode);

    /// <summary>Returns the folder used for a given asset type under a project.</summary>
    Task<ArtifactSaveResult> SaveArtifactAsync(
        string projectCode,
        AssetType assetType,
        string version,
        string title,
        string content,
        CancellationToken ct);
}

public sealed record ProjectFolderRef
{
    public required string ProjectFolderId { get; init; }
    public required string ProjectFolderUrl { get; init; }
    public required IReadOnlyDictionary<string, string> SubFolders { get; init; }
}

public sealed record DocumentRead
{
    public required string FileId { get; init; }
    public required string Content { get; init; }
    public required string Title { get; init; }
}

public sealed record DocumentWrite
{
    public required string FileId { get; init; }
    public required string Url { get; init; }
}

public sealed record FolderRef
{
    public required string FolderId { get; init; }
    public required string Url { get; init; }
}