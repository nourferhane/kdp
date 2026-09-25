using System.ComponentModel;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Web.Api;
using Zunavio.KdpFactory.Infrastructure.Google;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Web.Mcp;

[McpServerToolType]
[Authorize(Policy = McpSecurity.McpToolsPolicy)]
public sealed class ZunavioAssetTools(IUnitOfWork db, VerifiedImageUploadService uploader, IGoogleCredentialProvider google)
{
    [McpServerTool(Name = "zunavio_upload_image"),
     Description("Upload actual PNG/JPEG/WebP image bytes (base64) to the project's Drive folder; read back the bytes, register a Draft asset in PostgreSQL, and return the real Drive file ID and asset code. Does not approve visual QA.")]
    [Authorize(Policy = McpSecurity.McpWritePolicy)]
    public async Task<string> UploadImageAsync(
        [Description("Existing PostgreSQL project code, e.g. ZNV-002.")] string projectCode,
        [Description("Positive illustration page number, 1-999.")] int pageNumber,
        [Description("Actual generated image file encoded as base64 (no data URI prefix). Never send a local path or a placeholder.")] string imageBase64,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectCode))
            return JsonSerializer.Serialize(new { success = false, error = "project_code_required" });
        if (string.IsNullOrWhiteSpace(imageBase64) || imageBase64.Length > VerifiedImageUploadService.MaxBytes * 4 / 3 + 16)
            return JsonSerializer.Serialize(new { success = false, error = "invalid_file_size" });
        try
        {
            var bytes = Convert.FromBase64String(imageBase64);
            var mime = VerifiedImageUploadService.DetectMime(bytes);
            if (mime is null)
                return JsonSerializer.Serialize(new { success = false, error = "invalid_image_signature" });
            var result = await uploader.UploadAsync(projectCode, pageNumber, mime, bytes, ct);
            return JsonSerializer.Serialize(new { success = result.Success, error = result.ErrorCode,
                verified = result.Success, projectCode, pageNumber, assetCode = result.AssetCode,
                driveFileId = result.DriveFileId, driveUrl = result.DriveUrl, folderId = result.FolderId,
                fileName = result.FileName, mimeType = result.MimeType, size = result.Size,
                sha256 = result.Sha256, idempotent = result.Idempotent });
        }
        catch (FormatException)
        {
            return JsonSerializer.Serialize(new { success = false, error = "invalid_base64" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, error = "image_upload_failed", detail = ex.GetBaseException().Message });
        }
    }

    [McpServerTool(Name = "zunavio_verify_asset"), Description("Verify PostgreSQL registration and current physical Drive image bytes against the recorded SHA-256.")]
    public async Task<string> VerifyAssetAsync(
        [Description("Canonical asset code, for example ZNV-002-IMG-001.")] string assetCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assetCode))
            return JsonSerializer.Serialize(new { success = false, error = "asset_code_required" });

        var asset = await db.Assets.GetByCodeAsync(assetCode.Trim(), ct);
        if (asset is null)
            return JsonSerializer.Serialize(new { success = false, exists = false, assetCode = assetCode.Trim() });

        if (string.IsNullOrWhiteSpace(asset.DriveFileId))
            return JsonSerializer.Serialize(new { success = false, exists = true, error = "drive_file_missing", assetCode });
        try
        {
            var metadataRequest = google.Drive.Files.Get(asset.DriveFileId);
            metadataRequest.Fields = "id,mimeType,size,trashed";
            var file = await metadataRequest.ExecuteAsync(ct);
            if (file.Trashed == true || file.Id != asset.DriveFileId)
                return JsonSerializer.Serialize(new { success = false, exists = true, error = "drive_file_missing", assetCode });
            if (asset.AssetType != AssetType.Illustration)
                return JsonSerializer.Serialize(new { success = true, exists = true, verified = true,
                    verificationMode = "drive_metadata_only", assetCode = asset.AssetCode,
                    assetType = asset.AssetType.ToString(), status = asset.Status.ToString(),
                    qaStatus = asset.QaStatus.ToString(), driveFileId = asset.DriveFileId,
                    driveUrl = asset.DriveUrl, size = file.Size });
            if (file.Size is null or > VerifiedImageUploadService.MaxBytes)
                return JsonSerializer.Serialize(new { success = false, exists = true,
                    error = "invalid_file_size", assetCode });
            using var content = JsonDocument.Parse(asset.ContentJson ?? "{}");
            var expectedHash = content.RootElement.TryGetProperty("sha256", out var hash) ? hash.GetString() : null;
            if (string.IsNullOrWhiteSpace(expectedHash))
                return JsonSerializer.Serialize(new { success = false, exists = true, error = "asset_hash_missing", assetCode });
            await using var bytes = new MemoryStream();
            var downloaded = await google.Drive.Files.Get(asset.DriveFileId).DownloadAsync(bytes, ct);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
            if (downloaded.Status != Google.Apis.Download.DownloadStatus.Completed ||
                !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Serialize(new { success = false, exists = true, error = "drive_content_mismatch", assetCode });
            if (actualHash is "55299df0571185ffb58d39e8af480ae6f29b1f6f847a1daf1684c020b7bca90a"
                or "3ef925c01577a0f95e6eae6d6220178cf18735a2e8d42aa7292ab75074673f70")
                return JsonSerializer.Serialize(new { success = false, exists = true,
                    error = "diagnostic_placeholder_not_production_asset", assetCode, driveFileId = asset.DriveFileId });
            return JsonSerializer.Serialize(new
            {
                success = true, exists = true, verified = true, assetCode = asset.AssetCode,
                assetType = asset.AssetType.ToString(), status = asset.Status.ToString(),
                qaStatus = asset.QaStatus.ToString(), driveFileId = asset.DriveFileId,
                driveUrl = asset.DriveUrl, sha256 = actualHash, size = file.Size,
                createdAtUtc = asset.CreatedAt.ToUniversalTime()
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, exists = true,
                error = "asset_verification_failed", detail = ex.GetBaseException().Message, assetCode });
        }
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
