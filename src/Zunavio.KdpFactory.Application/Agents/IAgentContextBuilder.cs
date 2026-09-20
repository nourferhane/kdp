using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Agents;

/// <summary>
/// Builds only the context an agent actually needs (section 15). Never the
/// full project history; this controls token usage.
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
    private readonly FactorySettings _settings;

    public AgentContextBuilder(IUnitOfWork db, FactorySettings settings)
    {
        _db = db;
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
        var assets = await LoadApprovedAssetsAsync(project.Id, agentCode, ct);
        var previous = await LoadPreviousOutputsAsync(project, agentCode, ct);

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

    private static string ComputeInputVersion(IReadOnlyList<AgentInputAsset> assets) =>
        assets.Count == 0 ? "baseline" : string.Join(";", assets.Select(a => a.AssetCode));

    private async Task<IReadOnlyList<AgentInputAsset>> LoadApprovedAssetsAsync(Guid projectId, AgentCode agentCode, CancellationToken ct)
    {
        var types = RequiredAssetTypes(agentCode);
        var assets = new List<AgentInputAsset>();

        foreach (var type in types)
        {
            var latest = await _db.Assets.GetLatestAsync(projectId, type, ct);
            if (latest is null || latest.Status is AssetStatus.Superseded) continue;

            assets.Add(new AgentInputAsset
            {
                AssetType = type,
                AssetCode = latest.AssetCode,
                Version = latest.Version,
                DriveFileId = latest.DriveFileId,
                DriveUrl = latest.DriveUrl,
                ContentJson = latest.ContentJson ?? string.Empty,
            });
        }

        return assets;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadPreviousOutputsAsync(Project project, AgentCode agentCode, CancellationToken ct)
    {
        var relevant = RelevantAgents(agentCode);
        var outputs = new Dictionary<string, string>();

        foreach (var code in relevant)
        {
            var run = await _db.Runs.GetLatestCompleteForAgentAsync(project.Id, code, ct);
            if (run is { OutputJson: not null }) outputs[code.ToString()] = run.OutputJson;
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
}