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
/// Loads the authoritative prompt from Google Drive and records an immutable
/// snapshot on every run. When Google is configured, Drive is mandatory:
/// cached prompt text is never used as a silent fallback.
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

    public async Task<PromptSnapshot> LoadAsync(
        AgentDefinition definition,
        CancellationToken ct)
    {
        // Strict production path: if Google is configured, every agent run must
        // resolve and read the current Google Doc. Any Drive failure blocks the
        // run rather than silently executing an old cached prompt.
        if (_options.IsConfigured)
        {
            var promptFileId = await ResolvePromptFileIdAsync(definition, ct);
            if (string.IsNullOrWhiteSpace(promptFileId))
            {
                throw new InvalidOperationException(
                    $"No Google Drive prompt document could be resolved for agent '{definition.Code}'.");
            }

            return await FetchAndCacheAsync(definition, promptFileId, ct);
        }

        // Local/offline development may use a previously cached immutable
        // snapshot, but this path is never used when Drive is enabled.
        if (HasCachedPrompt(definition))
        {
            _logger.LogInformation(
                "Google Drive is not configured; using cached prompt for agent {Code} (v{Version}).",
                definition.Code,
                definition.PromptVersion);

            return NewSnapshot(definition);
        }

        throw new GoogleNotConfiguredException(
            $"No prompt is available for agent '{definition.Code}'. Configure Google Drive or seed a cached prompt for offline development.");
    }

    public async Task<PromptSnapshot> RefreshAsync(
        AgentDefinition definition,
        CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            throw new GoogleNotConfiguredException(
                "Drive is not configured; cannot refresh prompts. Add a service account credential.");
        }

        var promptFileId = await ResolvePromptFileIdAsync(definition, ct);
        if (string.IsNullOrWhiteSpace(promptFileId))
        {
            throw new InvalidOperationException(
                $"No Google Drive prompt document could be resolved for agent '{definition.Code}'.");
        }

        return await FetchAndCacheAsync(definition, promptFileId, ct);
    }

    private async Task<string?> ResolvePromptFileIdAsync(
        AgentDefinition definition,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(definition.PromptDriveFileId))
        {
            return definition.PromptDriveFileId;
        }

        if (string.IsNullOrWhiteSpace(_options.PromptsFolderId))
        {
            throw new InvalidOperationException(
                $"GOOGLE_PROMPTS_FOLDER_ID is required to discover the prompt for agent '{definition.Code}'.");
        }

        var request = _credentials.Drive.Files.List();
        request.Q =
            $"'{Escape(_options.PromptsFolderId)}' in parents and trashed=false and mimeType='application/vnd.google-apps.document'";
        request.PageSize = 100;
        request.Fields = "files(id,name)";
        var result = await request.ExecuteAsync(ct);

        // Prefer the exact naming convention of the factory prompt documents,
        // then fall back to code/name matching for backward compatibility.
        var expectedToken = OfficialPromptPrefix(definition.Code);

        var match = (result.Files ?? [])
            .FirstOrDefault(file =>
                (file.Name ?? string.Empty).Contains(
                    expectedToken,
                    StringComparison.OrdinalIgnoreCase))
            ?? (result.Files ?? [])
                .FirstOrDefault(file =>
                {
                    var name = file.Name ?? string.Empty;
                    return name.Contains(definition.Code, StringComparison.OrdinalIgnoreCase)
                        || name.Contains(definition.Name, StringComparison.OrdinalIgnoreCase);
                });

        if (match is null)
        {
            return null;
        }

        definition.PromptDriveFileId = match.Id;
        definition.PromptDriveUrl =
            $"https://docs.google.com/document/d/{match.Id}/edit";

        await _db.SaveChangesAsync(ct);

        return match.Id;
    }

    private async Task<PromptSnapshot> FetchAndCacheAsync(
        AgentDefinition definition,
        string fileId,
        CancellationToken ct)
    {
        var request = _credentials.Drive.Files.Get(fileId);
        request.Fields = "id,name,modifiedTime";
        var meta = await request.ExecuteAsync(ct);

        var read = await _storage.ReadDocumentAsync(fileId, ct);
        var text = read.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"Prompt document '{meta.Name ?? fileId}' for agent '{definition.Code}' is empty.");
        }

        var hash = Hash(text);
        var version = meta.ModifiedTime is { } modified
            ? $"v{modified.ToUniversalTime():yyyyMMdd-HHmm}"
            : $"v{hash[..6]}";

        definition.PromptDriveFileId = fileId;
        definition.PromptDriveUrl =
            $"https://docs.google.com/document/d/{fileId}/edit";
        definition.PromptVersion = version;
        definition.PromptHash = hash;
        definition.PromptTextCache = text;
        definition.PromptLastSyncedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Loaded authoritative Drive prompt for agent {Code} (v{Version}, {Bytes} bytes, {FileId}).",
            definition.Code,
            version,
            Encoding.UTF8.GetByteCount(text),
            fileId);

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

    private static string OfficialPromptPrefix(string code) =>
        code.Trim().ToUpperInvariant() switch
        {
            "ORCHESTRATOR" => "AGENT_00_ORCHESTRATOR",
            "SCOUT" => "AGENT_01_SCOUT",
            "VALIDATOR" => "AGENT_02_VALIDATOR",
            "ARCHITECT" => "AGENT_03_ARCHITECT",
            "WRITER" => "AGENT_04_WRITER",
            "ARTDIRECTOR" => "AGENT_05_ART_DIRECTOR",
            "ART_DIRECTOR" => "AGENT_05_ART_DIRECTOR",
            "PRODUCTION" => "AGENT_06_PRODUCTION",
            "METADATA" => "AGENT_07_METADATA",
            "QA" => "AGENT_08_QA",
            "LAUNCH" => "AGENT_09_LAUNCH",
            _ => string.Empty,
        };

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
