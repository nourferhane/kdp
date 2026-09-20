using System.Security.Cryptography;
using System.Text;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Infrastructure.Configuration;

namespace Zunavio.KdpFactory.Infrastructure.Google;

/// <summary>
/// Loads the authoritative prompt from a Google Doc and caches a snapshot in
/// the AgentDefinition table. Drive is the authoring source; fetching is
/// resilient: when Drive fails or is not configured, the cached copy is used.
/// </summary>
public sealed class GoogleAgentPromptLoader : IAgentPromptLoader
{
    private readonly IGoogleCredentialProvider _credentials;
    private readonly GoogleOptions _options;
    private readonly IArtifactStorage _storage;
    private readonly IUnitOfWork _db;
    private readonly ILogger<GoogleAgentPromptLoader> _logger;

    public GoogleAgentPromptLoader(
        IGoogleCredentialProvider credentials,
        IOptions<GoogleOptions> options,
        IArtifactStorage storage,
        IUnitOfWork db,
        ILogger<GoogleAgentPromptLoader> logger)
    {
        _credentials = credentials;
        _options = options.Value;
        _storage = storage;
        _db = db;
        _logger = logger;
    }

    public async Task<PromptSnapshot> LoadAsync(AgentDefinition definition, CancellationToken ct)
    {
        var promptFileId = await ResolvePromptFileIdAsync(definition);
        if (promptFileId is not null && _options.IsConfigured)
        {
            try
            {
                return await FetchAndCacheAsync(definition, promptFileId, ct);
            }
            catch (Exception ex) when (HasCachedPrompt(definition))
            {
                _logger.LogWarning(ex,
                    "Could not load prompt for agent {Code} from Drive; using cached copy (v{Version}).",
                    definition.Code, definition.PromptVersion);
            }
        }
        else if (promptFileId is not null && !_options.IsConfigured)
        {
            _logger.LogInformation(
                "Drive not configured; using cached prompt for agent {Code} (v{Version}).",
                definition.Code, definition.PromptVersion);
        }

        if (HasCachedPrompt(definition))
        {
            return NewSnapshot(definition);
        }

        throw new InvalidOperationException(
            $"No prompt available for agent '{definition.Code}'. Configure Google and seed prompt doc IDs, " +
            "or provide a cached PromptTextCache first.");
    }

    public async Task<PromptSnapshot> RefreshAsync(AgentDefinition definition, CancellationToken ct)
    {
        var promptFileId = await ResolvePromptFileIdAsync(definition);
        if (promptFileId is null)
        {
            if (HasCachedPrompt(definition))
            {
                return NewSnapshot(definition);
            }

            throw new InvalidOperationException($"No prompt Drive file id is recorded for agent '{definition.Code}'.");
        }

        if (!_options.IsConfigured)
        {
            throw new GoogleNotConfiguredException(
                "Drive is not configured; cannot refresh prompts. Add a service account credential.");
        }

        return await FetchAndCacheAsync(definition, promptFileId, ct);
    }

    private async Task<string?> ResolvePromptFileIdAsync(AgentDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.PromptDriveFileId))
        {
            return definition.PromptDriveFileId;
        }

        // Discover by name in the prompts folder (e.g. the repo document).
        if (_options.IsConfigured && !string.IsNullOrWhiteSpace(_options.PromptsFolderId))
        {
            var drive = _credentials.Drive;
            var request = drive.Files.List();
            request.Q = $"'{Escape(_options.PromptsFolderId)}' in parents and trashed=false and mimeType='application/vnd.google-apps.document'";
            request.PageSize = 100;
            request.Fields = "files(id,name)";
            var result = await request.ExecuteAsync(CancellationToken.None);

            foreach (var file in result.Files ?? [])
            {
                var name = file.Name ?? string.Empty;
                if (name.Contains(definition.Code, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(definition.Name, StringComparison.OrdinalIgnoreCase))
                {
                    definition.PromptDriveFileId = file.Id;
                    definition.PromptDriveUrl = $"https://docs.google.com/document/d/{file.Id}/edit";
                    return file.Id;
                }
            }
        }

        return null;
    }

    private async Task<PromptSnapshot> FetchAndCacheAsync(AgentDefinition definition, string fileId, CancellationToken ct)
    {
        var drive = _credentials.Drive;
        var request = drive.Files.Get(fileId);
        request.Fields = "id,name,modifiedTime";
        var meta = await request.ExecuteAsync(ct);

        var read = await _storage.ReadDocumentAsync(fileId, ct);
        var text = read.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (HasCachedPrompt(definition))
            {
                _logger.LogWarning("Prompt document {FileId} for agent {Code} is empty; using cached copy.", fileId, definition.Code);
                return NewSnapshot(definition);
            }

            throw new InvalidOperationException($"Prompt document for agent '{definition.Code}' is empty.");
        }

        var hash = Hash(text);
        var version = meta.ModifiedTime is { } modified
            ? $"v{modified.ToUniversalTime():yyyyMMdd-HHmm}"
            : $"v{hash[..6]}";

        definition.PromptDriveFileId = fileId;
        definition.PromptDriveUrl ??= $"https://docs.google.com/document/d/{fileId}/edit";
        definition.PromptVersion = version;
        definition.PromptHash = hash;
        definition.PromptTextCache = text;
        definition.PromptLastSyncedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cached prompt for agent {Code} (v{Version}, {Bytes} bytes).",
            definition.Code, version, Encoding.UTF8.GetByteCount(text));

        return new PromptSnapshot
        {
            Text = text,
            Version = version,
            Hash = hash,
            DriveFileId = fileId,
        };
    }

    private static bool HasCachedPrompt(AgentDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.PromptTextCache)
        && !string.IsNullOrWhiteSpace(definition.PromptHash);

    private static PromptSnapshot NewSnapshot(AgentDefinition definition) => new()
    {
        Text = definition.PromptTextCache!,
        Version = definition.PromptVersion ?? $"v{definition.PromptHash?[..6]}",
        Hash = definition.PromptHash!,
        DriveFileId = definition.PromptDriveFileId ?? string.Empty,
    };

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}