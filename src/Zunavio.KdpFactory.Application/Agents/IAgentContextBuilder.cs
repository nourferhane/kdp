using System.Security.Cryptography;
using System.Text;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Agents;

/// <summary>
/// Builds only the context an agent actually needs. Approved Google Drive
/// documents are authoritative whenever Drive is configured.
/// </summary>
public interface IAgentContextBuilder
{
    Task<AgentExecutionContext> BuildAsync(
        Project project,
        AgentDefinition agent,
        PromptSnapshot prompt,
        string idempotencyKey,
        CancellationToken ct);
}

public sealed class AgentContextBuilder : IAgentContextBuilder
{
    private readonly IUnitOfWork _db;
    private readonly IArtifactStorage _artifacts;
    private readonly FactorySettings _settings;

    public AgentContextBuilder(
        IUnitOfWork db,
        IArtifactStorage artifacts,
        FactorySettings settings)
    {
        _db = db;
        _artifacts = artifacts;
        _settings = settings;
    }

    public async Task<AgentExecutionContext> BuildAsync(
        Project project,
        AgentDefinition agent,
        PromptSnapshot prompt,
        string idempotencyKey,
        CancellationToken ct)
    {
        var agentCode = agent.ToAgentCode();

        // Strict execution order:
        // 1) prompt snapshot was loaded from Drive by IAgentPromptLoader
        // 2) approved upstream assets are now loaded from their Drive docs
        // 3) only non-duplicated predecessor outputs are added
        var assets = await LoadApprovedAssetsAsync(project.Id, agentCode, ct);
        var previous = await LoadPreviousOutputsAsync(project, agentCode, assets, ct);

        return new AgentExecutionContext
        {
            Project = project,
            AgentDefinition = agent,
            PromptSnapshot = prompt.Text,
            PromptVersion = prompt.Version,
            PromptHash = prompt.Hash,
            ApprovedAssets = assets,
            PreviousAgentOutputs = previous,
            FactorySettings = _settings,
            Model = _settings.DefaultModel,
            InputVersion = ComputeInputVersion(assets),
            IdempotencyKey = idempotencyKey,
        };
    }

    private static string ComputeInputVersion(IReadOnlyList<AgentInputAsset> assets)
    {
        if (assets.Count == 0) return "baseline";

        return string.Join(
            ";",
            assets.Select(a =>
                $"{a.AssetCode}@{a.Version}#{Hash(a.ContentJson)[..12]}"));
    }

    private async Task<IReadOnlyList<AgentInputAsset>> LoadApprovedAssetsAsync(
        Guid projectId,
        AgentCode agentCode,
        CancellationToken ct)
    {
        var types = RequiredAssetTypes(agentCode);
        var assets = new List<AgentInputAsset>();

        foreach (var type in types)
        {
            // Never feed Draft/Superseded content to a downstream agent.
            var latest = await _db.Assets.GetLatestApprovedAsync(projectId, type, ct);
            if (latest is null) continue;

            var content = latest.ContentJson ?? string.Empty;

            // A Drive-backed approved asset must always be re-read from Drive.
            // We never silently substitute the PostgreSQL snapshot.
            if (!string.IsNullOrWhiteSpace(latest.DriveFileId))
            {
                if (!_settings.GoogleEnabled)
                {
                    throw new InvalidOperationException(
                        $"Approved asset '{latest.AssetCode}' is Drive-backed but Google integration is disabled.");
                }

                var document = await _artifacts.ReadDocumentAsync(latest.DriveFileId, ct);
                if (string.IsNullOrWhiteSpace(document.Content))
                {
                    throw new InvalidOperationException(
                        $"Approved asset '{latest.AssetCode}' has an empty Google Drive document '{latest.DriveFileId}'.");
                }

                content = document.Content;
            }
            else if (_settings.GoogleEnabled)
            {
                throw new InvalidOperationException(
                    $"Approved asset '{latest.AssetCode}' has no Google Drive file id while strict Google mode is enabled.");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"Approved asset '{latest.AssetCode}' has no usable content.");
            }

            assets.Add(new AgentInputAsset
            {
                AssetType = type,
                AssetCode = latest.AssetCode,
                Version = latest.Version,
                DriveFileId = latest.DriveFileId,
                DriveUrl = latest.DriveUrl,
                ContentJson = content,
            });
        }

        return assets;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadPreviousOutputsAsync(
        Project project,
        AgentCode agentCode,
        IReadOnlyList<AgentInputAsset> approvedAssets,
        CancellationToken ct)
    {
        var relevant = RelevantAgents(agentCode);
        var outputs = new Dictionary<string, string>();
        var authoritativeAssetTypes = approvedAssets.Select(a => a.AssetType).ToHashSet();

        foreach (var code in relevant)
        {
            // If the predecessor has already produced an approved asset that is
            // present in the context, do not also inject its historical run JSON.
            // The approved asset is the single source of truth.
            var producedType = ProducedAssetType(code);
            if (producedType is not null && authoritativeAssetTypes.Contains(producedType.Value))
            {
                continue;
            }

            var run = await _db.Runs.GetLatestCompleteForAgentAsync(project.Id, code, ct);
            if (run is { OutputJson: not null })
            {
                outputs[code.ToString()] = run.OutputJson;
            }
        }

        return outputs;
    }

    private static IReadOnlyList<AssetType> RequiredAssetTypes(AgentCode agentCode) => agentCode switch
    {
        AgentCode.Scout => [],
        AgentCode.Validator => [AssetType.ScoutReport],
        AgentCode.Architect => [AssetType.ValidatorReport],
        AgentCode.Writer => [AssetType.ProductArchitecture],
        AgentCode.ArtDirector => [AssetType.ProductArchitecture, AssetType.Manuscript],
        AgentCode.Production => [AssetType.ProductArchitecture, AssetType.Manuscript, AssetType.VisualBible, AssetType.ScenePlan],
        AgentCode.Metadata => [AssetType.ProductArchitecture, AssetType.Manuscript],
        AgentCode.Qa => [AssetType.ProductArchitecture, AssetType.Manuscript, AssetType.VisualBible, AssetType.ScenePlan, AssetType.InteriorPdf, AssetType.Cover, AssetType.Metadata],
        AgentCode.Launch => [AssetType.InteriorPdf, AssetType.Cover, AssetType.Epub, AssetType.QaReport],
        _ => [],
    };

    private static IReadOnlyList<AgentCode> RelevantAgents(AgentCode agentCode) => agentCode switch
    {
        AgentCode.Validator => [AgentCode.Scout],
        AgentCode.Architect => [AgentCode.Validator],
        AgentCode.Writer => [AgentCode.Architect],
        AgentCode.ArtDirector => [AgentCode.Writer],
        AgentCode.Production => [AgentCode.ArtDirector],
        AgentCode.Metadata => [AgentCode.Production],
        AgentCode.Qa => [AgentCode.Architect, AgentCode.Writer, AgentCode.ArtDirector, AgentCode.Production, AgentCode.Metadata],
        AgentCode.Launch => [AgentCode.Qa],
        _ => [],
    };

    private static AssetType? ProducedAssetType(AgentCode agentCode) => agentCode switch
    {
        AgentCode.Scout => AssetType.ScoutReport,
        AgentCode.Validator => AssetType.ValidatorReport,
        AgentCode.Architect => AssetType.ProductArchitecture,
        AgentCode.Writer => AssetType.Manuscript,
        AgentCode.ArtDirector => AssetType.VisualBible,
        AgentCode.Production => AssetType.InteriorPdf,
        AgentCode.Metadata => AssetType.Metadata,
        AgentCode.Qa => AssetType.QaReport,
        AgentCode.Launch => AssetType.Epub,
        _ => null,
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
