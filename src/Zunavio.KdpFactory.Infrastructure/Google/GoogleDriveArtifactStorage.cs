using System.Collections.Concurrent;
using Google.Apis.Docs.v1.Data;
using Google.Apis.Drive.v3;
using Google.Apis.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Zunavio.KdpFactory.Infrastructure.Google;

/// <summary>
/// <see cref="IArtifactStorage"/> over Google Drive + Docs (section 16).
/// All folder lookups are cached in-process; failures are surfaced so the
/// caller (best-effort) can degrade gracefully.
/// </summary>
public sealed class GoogleDriveArtifactStorage : IArtifactStorage
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string DocMime = "application/vnd.google-apps.document";

    private static readonly string[] RootFolders =
    [
        "00_CONTROL_CENTER",
        "01_PROMPTS",
    ];

    private static readonly (string Name, AssetType Type)[] ProjectSubfolders =
    [
        ("01_RESEARCH", AssetType.ScoutReport),
        ("02_ARCHITECTURE", AssetType.ProductArchitecture),
        ("03_MANUSCRIPT", AssetType.Manuscript),
        ("04_VISUAL_BIBLE", AssetType.VisualBible),
        ("05_PROD_FILES", AssetType.InteriorPdf),
        ("06_METADATA", AssetType.Metadata),
        ("07_QA", AssetType.QaReport),
        ("08_LAUNCH", AssetType.Epub),
    ];

    private readonly IGoogleCredentialProvider _credentials;
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleDriveArtifactStorage> _logger;
    private readonly ConcurrentDictionary<string, string?> _folderIds = new(StringComparer.Ordinal);

    public GoogleDriveArtifactStorage(
        IGoogleCredentialProvider credentials,
        IOptions<GoogleOptions> options,
        ILogger<GoogleDriveArtifactStorage> logger)
    {
        _credentials = credentials;
        _options = options.Value;
        _logger = logger;
    }

    private DriveService Drive
    {
        get
        {
            if (!_options.IsConfigured || string.IsNullOrWhiteSpace(_options.RootFolderId))
            {
                throw new GoogleNotConfiguredException(
                    "Google Drive is not configured. Set a service account and GOOGLE_ROOT_FOLDER_ID.");
            }

            return _credentials.Drive;
        }
    }

    public Task<string> EnsureRootStructureAsync(CancellationToken ct)
    {
        var root = Drill(Drive, _options.RootFolderId!, RootFolders, ct);
        return Task.FromResult(root);
    }

    public async Task<ProjectFolderRef> EnsureProjectFoldersAsync(string projectCode, CancellationToken ct)
    {
        var drive = Drive;
        var root = await EnsureRootStructureAsync(ct);
        var projectFolderId = await FindOrCreateFolderAsync(drive, root, projectCode, ct);
        var projectFolderUrl = $"https://drive.google.com/drive/folders/{projectFolderId}";

        var subFolders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, _) in ProjectSubfolders)
        {
            if (subFolders.ContainsKey(name)) continue;
            var id = await FindOrCreateFolderAsync(drive, projectFolderId, name, ct);
            subFolders[name] = id;
        }

        return new ProjectFolderRef
        {
            ProjectFolderId = projectFolderId,
            ProjectFolderUrl = projectFolderUrl,
            SubFolders = subFolders,
        };
    }

    public async Task<DocumentRead> ReadDocumentAsync(string fileId, CancellationToken ct)
    {
        var doc = await _credentials.Docs.Documents.Get(fileId).ExecuteAsync(ct);
        var content = string.Join("\n", doc.Body.Content
            .Where(el => el.Paragraph?.Elements is not null && el.Paragraph.Elements.Count > 0)
            .Select(el => string.Concat(el.Paragraph.Elements
                .Where(e => e.TextRun?.Content is not null)
                .Select(e => e.TextRun.Content))
                .TrimEnd()));

        return new DocumentRead
        {
            FileId = fileId,
            Title = doc.Title ?? string.Empty,
            Content = content,
        };
    }

    public async Task<DocumentWrite> CreateDocumentAsync(
        string folderId, string title, string markdownContent, CancellationToken ct)
    {
        var drive = Drive;

        var file = new DriveFile
        {
            Name = title,
            MimeType = DocMime,
            Parents = [folderId],
        };
        var created = await drive.Files.Create(file).ExecuteAsync(ct);
        var fileId = created.Id ?? throw new InvalidOperationException("File creation returned no id.");

        if (!string.IsNullOrWhiteSpace(markdownContent))
        {
            const int index = 0;
            var requests = new List<Request>
            {
                new()
                {
                    InsertText = new InsertTextRequest
                    {
                        Text = markdownContent.EndsWith('\n') ? markdownContent : markdownContent + "\n",
                        Location = new Location { Index = index },
                    },
                },
            };

            await _credentials.Docs.Documents.BatchUpdate(
                new BatchUpdateDocumentRequest { Requests = requests }, fileId)
                .ExecuteAsync(ct);
        }

        _logger.LogInformation("Created Google Doc '{Title}' ({Id}) in folder {FolderId}.", title, fileId, folderId);

        return new DocumentWrite
        {
            FileId = fileId,
            Url = $"https://docs.google.com/document/d/{fileId}/edit",
        };
    }

    public async Task<FolderRef> CreateFolderAsync(string parentFolderId, string name, CancellationToken ct)
    {
        var file = new DriveFile
        {
            Name = name,
            MimeType = FolderMime,
            Parents = [parentFolderId],
        };
        var created = await Drive.Files.Create(file).ExecuteAsync(ct);
        var folderId = created.Id ?? throw new InvalidOperationException("Folder creation returned no id.");
        return new FolderRef
        {
            FolderId = folderId,
            Url = $"https://drive.google.com/drive/folders/{folderId}",
        };
    }

    public async Task<string?> FindFolderIdAsync(string rootFolderId, string name, CancellationToken ct)
    {
        var cacheKey = $"{rootFolderId}|{name}";
        if (_folderIds.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var drive = Drive;
        var query = $"'{Escape(rootFolderId)}' in parents and mimeType='{FolderMime}' and name='{Escape(name)}' and trashed=false";
        var request = drive.Files.List();
        request.Q = query;
        request.PageSize = 1;
        request.Fields = "files(id,name)";

        var result = await request.ExecuteAsync(ct);
        var id = result.Files is { Count: > 0 } ? result.Files[0].Id : null;
        _folderIds[cacheKey] = id;
        return id;
    }

    public string BuildArtifactName(string projectCode, AssetType assetType, string version)
    {
        var assetTypeName = Enum.GetName(assetType)?.ToUpperInvariant() ?? assetType.ToString().ToUpperInvariant();
        return $"{projectCode}_{assetTypeName}_v{version}";
    }

    public string FolderNameFor(AssetType assetType, string projectCode)
    {
        var match = ProjectSubfolders.FirstOrDefault(s => s.Type == assetType);
        return match.Name ?? "00_MISC";
    }

    public async Task<ArtifactSaveResult> SaveArtifactAsync(
        string projectCode, AssetType assetType, string version, string title, string content, CancellationToken ct)
    {
        var project = await EnsureProjectFoldersAsync(projectCode, ct);
        var folderName = FolderNameFor(assetType, projectCode);
        if (!project.SubFolders.TryGetValue(folderName, out var folderId))
        {
            throw new InvalidOperationException(
                $"No subfolder '{folderName}' found for project '{projectCode}'. EnsureProjectFoldersAsync created folders.");
        }

        var doc = await CreateDocumentAsync(folderId, title, content, ct);
        return new ArtifactSaveResult
        {
            DriveFileId = doc.FileId,
            DriveUrl = doc.Url,
            FolderId = folderId,
            ProjectFolderId = project.ProjectFolderId,
        };
    }

    private string Drill(DriveService drive, string rootFolderId, IReadOnlyList<string> path, CancellationToken ct)
    {
        var current = rootFolderId;
        foreach (var segment in path)
        {
            var found = FindFolderIdAsync(current, segment, ct).GetAwaiter().GetResult();
            current = found ?? CreateFolderAsync(current, segment, ct).GetAwaiter().GetResult().FolderId;
        }

        return current;
    }

    private async Task<string> FindOrCreateFolderAsync(DriveService drive, string parent, string name, CancellationToken ct)
    {
        var existing = await FindFolderIdAsync(parent, name, ct);
        if (existing is not null)
        {
            return existing;
        }

        var folder = await CreateFolderAsync(parent, name, ct);
        _folderIds[$"{parent}|{name}"] = folder.FolderId;
        return folder.FolderId;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}