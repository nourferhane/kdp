using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Google;
using Zunavio.KdpFactory.Infrastructure.Persistence;

namespace Zunavio.KdpFactory.Infrastructure.Seeding;

public sealed record AgentSeedResult(int Created, int Updated);

/// <summary>
/// Idempotently seeds the agent catalog (section 10) and, when Google Drive is
/// configured, writes each agent's prompt as a Google Doc in the prompts folder
/// and records its Drive file id on the definition.
/// </summary>
public sealed class AgentDefinitionSeeder : IGoogleDriveAgentDefinitionSeeder
{
    private readonly KdpDbContext _db;
    private readonly IGoogleCredentialProvider _credentials;
    private readonly IArtifactStorage _storage;
    private readonly GoogleOptions _options;
    private readonly ILogger<AgentDefinitionSeeder> _logger;

    public AgentDefinitionSeeder(
        KdpDbContext db,
        IGoogleCredentialProvider credentials,
        IArtifactStorage storage,
        IOptions<GoogleOptions> options,
        ILogger<AgentDefinitionSeeder> logger)
    {
        _db = db;
        _credentials = credentials;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SeedPromptFileIdsAsync(CancellationToken ct)
    {
        var result = await SeedAsync(ct);
        return result.Created + result.Updated;
    }

    public async Task<AgentSeedResult> SeedAsync(CancellationToken ct)
    {
        var created = 0;
        var updated = 0;

        foreach (var definition in AgentDefinitionSeedData.All)
        {
            var existing = await _db.AgentDefinitions.FirstOrDefaultAsync(a => a.Code == definition.Code, ct);
            if (existing is null)
            {
                _db.AgentDefinitions.Add(definition);
                created++;
            }
            else
            {
                existing.Name = definition.Name;
                existing.Description = definition.Description;
                existing.Enabled = definition.Enabled;
                if (string.IsNullOrWhiteSpace(existing.PromptTextCache))
                {
                    existing.PromptTextCache = definition.PromptTextCache;
                }

                updated++;
            }
        }

        await _db.SaveChangesAsync(ct);

        if (_options.IsConfigured && !string.IsNullOrWhiteSpace(_options.PromptsFolderId))
        {
            foreach (var definition in await _db.AgentDefinitions.ToListAsync(ct))
            {
                await SeedPromptDocumentAsync(definition, ct);
            }

            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Agent definitions seeded: {Created} created, {Updated} updated.", created, updated);
        return new AgentSeedResult(created, updated);
    }

    private async Task SeedPromptDocumentAsync(AgentDefinition definition, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(definition.PromptDriveFileId))
        {
            return;
        }

        var docName = $"ZUNAVIO_AGENT_{definition.Code}_PROMPT";

        var request = _credentials.Drive.Files.List();
        request.Q = $"'{Escape(_options.PromptsFolderId!)}' in parents and trashed=false and name='{Escape(docName)}'";
        request.PageSize = 1;
        request.Fields = "files(id,name)";
        var result = await request.ExecuteAsync(ct);

        if (result.Files is { Count: > 0 } && result.Files[0].Id is { } id)
        {
            definition.PromptDriveFileId = id;
            definition.PromptDriveUrl = $"https://docs.google.com/document/d/{id}/edit";
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.PromptTextCache))
        {
            _logger.LogWarning("No prompt content to seed for agent {Code}.", definition.Code);
            return;
        }

        var doc = await _storage.CreateDocumentAsync(
            _options.PromptsFolderId!, docName, definition.PromptTextCache, ct);
        definition.PromptDriveFileId = doc.FileId;
        definition.PromptDriveUrl = doc.Url;
        _logger.LogInformation("Created prompt document for agent {Code}: {Url}", definition.Code, doc.Url);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}