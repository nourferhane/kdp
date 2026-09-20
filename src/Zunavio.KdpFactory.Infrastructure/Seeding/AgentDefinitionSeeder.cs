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
/// Idempotently seeds the agent catalog and, when Google Drive is configured,
/// discovers the authoritative AGENT_00...AGENT_09 prompt documents that
/// already exist in GOOGLE_PROMPTS_FOLDER_ID. It never creates replacement
/// prompt documents from cached/default text.
/// </summary>
public sealed class AgentDefinitionSeeder : IGoogleDriveAgentDefinitionSeeder
{
    private readonly KdpDbContext _db;
    private readonly IGoogleCredentialProvider _credentials;
    private readonly GoogleOptions _options;
    private readonly ILogger<AgentDefinitionSeeder> _logger;

    public AgentDefinitionSeeder(
        KdpDbContext db,
        IGoogleCredentialProvider credentials,
        IOptions<GoogleOptions> options,
        ILogger<AgentDefinitionSeeder> logger)
    {
        _db = db;
        _credentials = credentials;
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
            var existing = await _db.AgentDefinitions
                .FirstOrDefaultAsync(a => a.Code == definition.Code, ct);

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
            await DiscoverOfficialPromptDocumentsAsync(ct);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Agent definitions seeded: {Created} created, {Updated} updated.",
            created,
            updated);

        return new AgentSeedResult(created, updated);
    }

    private async Task DiscoverOfficialPromptDocumentsAsync(CancellationToken ct)
    {
        var request = _credentials.Drive.Files.List();
        request.Q =
            $"'{Escape(_options.PromptsFolderId!)}' in parents and trashed=false and mimeType='application/vnd.google-apps.document'";
        request.PageSize = 100;
        request.Fields = "files(id,name)";
        var result = await request.ExecuteAsync(ct);
        var files = result.Files ?? [];

        foreach (var definition in await _db.AgentDefinitions.ToListAsync(ct))
        {
            var expectedPrefix = OfficialPromptPrefix(definition.Code);
            if (string.IsNullOrWhiteSpace(expectedPrefix))
            {
                _logger.LogWarning(
                    "No official prompt naming rule is defined for agent {Code}.",
                    definition.Code);
                continue;
            }

            var match = files.FirstOrDefault(file =>
                !string.IsNullOrWhiteSpace(file.Name)
                && file.Name.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is null)
            {
                _logger.LogWarning(
                    "Official prompt '{ExpectedPrefix}...' was not found in GOOGLE_PROMPTS_FOLDER_ID for agent {Code}. No replacement document was created.",
                    expectedPrefix,
                    definition.Code);
                continue;
            }

            definition.PromptDriveFileId = match.Id;
            definition.PromptDriveUrl =
                $"https://docs.google.com/document/d/{match.Id}/edit";

            _logger.LogInformation(
                "Linked agent {Code} to official prompt '{PromptName}' ({PromptId}).",
                definition.Code,
                match.Name,
                match.Id);
        }
    }

    internal static string OfficialPromptPrefix(string code) =>
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

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
