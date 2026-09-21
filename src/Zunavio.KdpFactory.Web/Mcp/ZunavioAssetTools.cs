using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Web.Mcp;

[McpServerToolType]
public sealed class ZunavioAssetTools(IUnitOfWork db)
{
    [McpServerTool(Name = "zunavio_verify_asset"), Description("Verify that a Zunavio asset is registered after physical Drive verification.")]
    public async Task<string> VerifyAssetAsync(
        [Description("Canonical asset code, for example ZNV-002-IMG-001.")] string assetCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assetCode))
            return JsonSerializer.Serialize(new { success = false, error = "asset_code_required" });

        var asset = await db.Assets.GetByCodeAsync(assetCode.Trim(), ct);
        if (asset is null)
            return JsonSerializer.Serialize(new { success = false, exists = false, assetCode = assetCode.Trim() });

        return JsonSerializer.Serialize(new
        {
            success = true, exists = true, assetCode = asset.AssetCode,
            assetType = asset.AssetType.ToString(), status = asset.Status.ToString(),
            qaStatus = asset.QaStatus.ToString(), driveFileId = asset.DriveFileId,
            driveUrl = asset.DriveUrl, contentJson = asset.ContentJson,
            createdAtUtc = asset.CreatedAt.ToUniversalTime()
        });
    }

    [McpServerTool(Name = "zunavio_get_project_assets"), Description("List registered assets for a Zunavio project.")]
    public async Task<string> GetProjectAssetsAsync(
        [Description("Project code, for example ZNV-002.")] string projectCode,
        CancellationToken ct)
    {
        var project = await db.Projects.GetByCodeAsync(projectCode.Trim(), ct);
        if (project is null)
            return JsonSerializer.Serialize(new { success = false, error = "project_not_found", projectCode });

        var assets = await db.Assets.GetByProjectAsync(project.Id, ct);
        return JsonSerializer.Serialize(new
        {
            success = true, projectCode = project.ProjectCode, count = assets.Count,
            assets = assets.Select(a => new
            {
                assetCode = a.AssetCode, assetType = a.AssetType.ToString(),
                status = a.Status.ToString(), qaStatus = a.QaStatus.ToString(),
                driveFileId = a.DriveFileId, driveUrl = a.DriveUrl, contentJson = a.ContentJson
            })
        });
    }
}
