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
/// IArtifactStorage over the existing Zunavio Google Drive factory.
/// The configured root/project folders are reused; this class must never
/// manufacture an alternate folder hierarchy for an existing project.
/// </summary>
public sealed class GoogleDriveArtifactStorage : IArtifactStorage
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string DocMime = "application/vnd.google-apps.document";

    // Keep these names aligned with the real ZUNAVIO KDP FACTORY layout.
    private static readonly (string Name, AssetType Type)[] ProjectSubfolders =
    [
        ("01_RESEARCH", AssetType.ScoutReport),
        ("02_VALIDATION", AssetType.ValidatorReport),
        ("03_ARCHITECTURE", AssetType.ProductArchitecture),
        ("04_MANUSCRIPT", AssetType.Manuscript),
        ("05_VISUALS", AssetType.VisualBible),
        ("05_VISUALS", AssetType.ScenePlan),
        ("05_VISUALS", AssetType.Cover),
        ("06_PRODUCTION", AssetType.InteriorPdf),
        ("06_PRODUCTION", AssetType.Epub),
        ("07_METADATA", AssetType.Metadata),
        ("08_QA", AssetType.QaReport),
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
        // Accessing Drive validates that Google is configured. The root folder
        // itself already exists and is the authoritative factory root.
        _ = Drive;
        return Task.FromResult(_options.RootFolderId!);
    }

    public async Task<ProjectFolderRef> EnsureProjectFoldersAsync(string projectCode, CancellationToken ct)
    {
        var drive = Drive;
        var root = await EnsureRootStructureAsync(ct);

        // For the pilot project, GOOGLE_PROJECT_FOLDER_ID points to the already
        // existing ZNV-001 folder. For future projects, discover by code under
        // the root before creating anything new.
        var projectFolderId = await ResolveConfiguredProjectFolderAsync(drive, projectCode, ct)
            ?? await FindProjectFolderIdAsync(drive, root, projectCode, ct)
            ?? (await CreateFolderAsync(root, projectCode, ct)).FolderId;

        var projectFolderUrl = $"https://drive.google.com/drive/folders/{projectFolderId}";

        var subFolders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, _) in ProjectSubfolders)
        {
            if (subFolders.ContainsKey(name)) continue;

            var id = await FindOrCreateFolderAsync(projectFolderId, name, ct);
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
        string folderId,
        string title,
        string markdownContent,
        CancellationToken ct)
    {
        var file = new DriveFile
        {
            Name = title,
            MimeType = DocMime,
            Parents = [folderId],
        };

        var created = await Drive.Files.Create(file).ExecuteAsync(ct);
        var fileId = created.Id
            ?? throw new InvalidOperationException("File creation returned no id.");

        if (!string.IsNullOrWhiteSpace(markdownContent))
        {
            // Google Docs bodies begin at index 1; index 0 is invalid.
            const int index = 1;
            var requests = new List<Request>
            {
                new()
                {
                    InsertText = new InsertTextRequest
                    {
                        Text = markdownContent.EndsWith('\n')
                            ? markdownContent
                            : markdownContent + "\n",
                        Location = new Location { Index = index },
                    },
                },
            };

            await _credentials.Docs.Documents.BatchUpdate(
                    new BatchUpdateDocumentRequest { Requests = requests },
                    fileId)
                .ExecuteAsync(ct);
        }

        _logger.LogInformation(
            "Created Google Doc '{Title}' ({Id}) in folder {FolderId}.",
            title,
            fileId,
            folderId);

        return new DocumentWrite
        {
            FileId = fileId,
            Url = $"https://docs.google.com/document/d/{fileId}/edit",
        };
    }

    public async Task<FolderRef> CreateFolderAsync(
        string parentFolderId,
        string name,
        CancellationToken ct)
    {
        var file = new DriveFile
        {
            Name = name,
            MimeType = FolderMime,
            Parents = [parentFolderId],
        };

        var created = await Drive.Files.Create(file).ExecuteAsync(ct);
        var folderId = created.Id
            ?? throw new InvalidOperationException("Folder creation returned no id.");

        return new FolderRef
        {
            FolderId = folderId,
            Url = $"https://drive.google.com/drive/folders/{folderId}",
        };
    }

    public async Task<string?> FindFolderIdAsync(
        string rootFolderId,
        string name,
        CancellationToken ct)
    {
        var cacheKey = $"{rootFolderId}|{name}";
        if (_folderIds.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var query =
            $"'{Escape(rootFolderId)}' in parents and mimeType='{FolderMime}' and name='{Escape(name)}' and trashed=false";

        var request = Drive.Files.List();
        request.Q = query;
        request.PageSize = 1;
        request.Fields = "files(id,name)";

        var result = await request.ExecuteAsync(ct);
        var id = result.Files is { Count: > 0 } ? result.Files[0].Id : null;

        _folderIds[cacheKey] = id;
        return id;
    }

    public string BuildArtifactName(
        string projectCode,
        AssetType assetType,
        string version)
    {
        var assetTypeName =
            Enum.GetName(assetType)?.ToUpperInvariant()
            ?? assetType.ToString().ToUpperInvariant();

        return $"{projectCode}_{assetTypeName}_v{version}";
    }

    public string FolderNameFor(AssetType assetType, string projectCode)
    {
        var match = ProjectSubfolders.FirstOrDefault(s => s.Type == assetType);
        return match.Name ?? "00_MISC";
    }

    public async Task<ArtifactSaveResult> SaveArtifactAsync(
        string projectCode,
        AssetType assetType,
        string version,
        string title,
        string content,
        CancellationToken ct)
    {
        var project = await EnsureProjectFoldersAsync(projectCode, ct);
        var folderName = FolderNameFor(assetType, projectCode);

        if (!project.SubFolders.TryGetValue(folderName, out var folderId))
        {
            throw new InvalidOperationException(
                $"No subfolder '{folderName}' found for project '{projectCode}'.");
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

    private async Task<string?> ResolveConfiguredProjectFolderAsync(
        DriveService drive,
        string projectCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ProjectFolderId))
        {
            return null;
        }

        try
        {
            var request = drive.Files.Get(_options.ProjectFolderId);
            request.Fields = "id,name,mimeType,trashed";
            var file = await request.ExecuteAsync(ct);

            if (file.MimeType == FolderMime
                && file.Trashed != true
                && !string.IsNullOrWhiteSpace(file.Name)
                && file.Name.StartsWith(projectCode, StringComparison.OrdinalIgnoreCase))
            {
                return file.Id;
            }

            _logger.LogWarning(
                "Configured GOOGLE_PROJECT_FOLDER_ID points to '{Name}', which does not match project {ProjectCode}; discovering folder under root instead.",
                file.Name,
                projectCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not validate GOOGLE_PROJECT_FOLDER_ID for project {ProjectCode}; discovering folder under root instead.",
                projectCode);
        }

        return null;
    }

    private static async Task<string?> FindProjectFolderIdAsync(
        DriveService drive,
        string rootFolderId,
        string projectCode,
        CancellationToken ct)
    {
        var request = drive.Files.List();
        request.Q =
            $"'{Escape(rootFolderId)}' in parents and mimeType='{FolderMime}' and name contains '{Escape(projectCode)}' and trashed=false";
        request.PageSize = 100;
        request.Fields = "files(id,name)";

        var result = await request.ExecuteAsync(ct);
        return result.Files?
            .FirstOrDefault(f =>
                !string.IsNullOrWhiteSpace(f.Name)
                && f.Name.StartsWith(projectCode, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private async Task<string> FindOrCreateFolderAsync(
        string parent,
        string name,
        CancellationToken ct)
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

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
