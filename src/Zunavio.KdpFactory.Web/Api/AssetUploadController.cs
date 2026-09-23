using System.Security.Cryptography;
using System.Text.Json;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Google.Apis.Drive.v3;
using Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Infrastructure.Google;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Zunavio.KdpFactory.Web.Api;

[ApiController]
[Route("api/assets")]
public sealed class AssetUploadController : ControllerBase
{
    private const string ApiKeyHeader = "X-Asset-Api-Key";
    private readonly IArtifactStorage _storage;
    private readonly IGoogleCredentialProvider _google;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _db;
    private readonly IGoogleControlCenterSyncService _controlCenter;
    private readonly ILogger<AssetUploadController> _logger;

    public AssetUploadController(
        IArtifactStorage storage,
        IGoogleCredentialProvider google,
        IConfiguration configuration,
        IUnitOfWork db,
        IGoogleControlCenterSyncService controlCenter,
        ILogger<AssetUploadController> logger)
    {
        _storage = storage;
        _google = google;
        _configuration = configuration;
        _db = db;
        _controlCenter = controlCenter;
        _logger = logger;
    }

    [HttpPost("upload")]
    [AllowAnonymous]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string projectCode,
        [FromForm] int? pageNumber,
        CancellationToken ct)
    {
        if (!IsAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        if (file is null || file.Length == 0)
            return BadRequest(new { success = false, error = "empty_file" });

        if (string.IsNullOrWhiteSpace(projectCode))
            return BadRequest(new { success = false, error = "project_code_required" });

        var mime = file.ContentType?.ToLowerInvariant();
        if (mime is not ("image/png" or "image/jpeg" or "image/webp"))
            return BadRequest(new { success = false, error = "unsupported_media_type" });

        var registeredProject = await _db.Projects.GetByCodeAsync(projectCode.Trim(), ct);
        string visualsFolderId;
        if (!string.IsNullOrWhiteSpace(registeredProject?.DriveFolderId))
        {
            var folderRequest = _google.Drive.Files.Get(registeredProject.DriveFolderId);
            folderRequest.Fields = "id,mimeType,trashed";
            var folder = await folderRequest.ExecuteAsync(ct);
            if (folder.Trashed == true || folder.MimeType != "application/vnd.google-apps.folder")
                return Conflict(new { success = false, error = "project_folder_invalid" });
            visualsFolderId = await EnsureFolderAsync(folder.Id, "05_VISUALS", ct);
        }
        else
        {
            var project = await _storage.EnsureProjectFoldersAsync(projectCode.Trim(), ct);
            if (!project.SubFolders.TryGetValue("05_VISUALS", out var folderId))
                return StatusCode(500, new { success = false, error = "visuals_folder_missing" });
            visualsFolderId = folderId;
        }

        var generatedFolderId = await EnsureFolderAsync(visualsFolderId, "GENERATED", ct);
        var safeName = BuildFileName(projectCode.Trim(), pageNumber, file.FileName, mime);

        await using var input = file.OpenReadStream();
        await using var buffered = new MemoryStream();
        await input.CopyToAsync(buffered, ct);
        var bytes = buffered.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        buffered.Position = 0;

        // Idempotency: one canonical Drive file per project/page (or filename when no page).
        var existingDriveFile = await FindExistingFileAsync(generatedFolderId, safeName, ct);
        if (existingDriveFile is not null)
        {
            var existingRequest = _google.Drive.Files.Get(existingDriveFile.Id);
            existingRequest.Fields = "id,name,mimeType,size,parents,webViewLink,description,trashed";
            var existing = await existingRequest.ExecuteAsync(ct);
            if (existing.Trashed != true && existing.Description?.Contains($"sha256={sha256}", StringComparison.OrdinalIgnoreCase) == true)
            {
                await RegisterAssetAsync(projectCode.Trim(), pageNumber, existing, sha256, file.Length, ct);
                return Ok(new
                {
                    success = true, verified = true, idempotent = true,
                    projectId = projectCode.Trim(), pageNumber, fileName = existing.Name,
                    driveFileId = existing.Id, driveUrl = existing.WebViewLink,
                    folderId = generatedFolderId, mimeType = existing.MimeType,
                    size = existing.Size ?? file.Length, sha256
                });
            }

            // Same logical page, different bytes: replace the canonical file rather than create a duplicate.
            var update = _google.Drive.Files.Update(new DriveFile
            {
                Name = safeName,
                MimeType = mime,
                Description = $"Zunavio asset; project={projectCode.Trim()}; page={pageNumber?.ToString() ?? "n/a"}; sha256={sha256}"
            }, existingDriveFile.Id, buffered, mime);
            update.Fields = "id,name,mimeType,size,parents,webViewLink,createdTime,description";
            var updateProgress = await update.UploadAsync(ct);
            if (updateProgress.Status != Google.Apis.Upload.UploadStatus.Completed || update.ResponseBody?.Id is null)
                return StatusCode(502, new { success = false, error = "drive_update_failed", message = updateProgress.Exception?.Message });

            var replaced = update.ResponseBody;
            await RegisterAssetAsync(projectCode.Trim(), pageNumber, replaced, sha256, file.Length, ct);
            return Ok(new
            {
                success = true, verified = true, idempotent = false, replaced = true,
                projectId = projectCode.Trim(), pageNumber, fileName = replaced.Name,
                driveFileId = replaced.Id, driveUrl = replaced.WebViewLink,
                folderId = generatedFolderId, mimeType = replaced.MimeType,
                size = replaced.Size ?? file.Length, sha256
            });
        }

        var metadata = new DriveFile
        {
            Name = safeName,
            Parents = [generatedFolderId],
            MimeType = mime,
            Description = $"Zunavio asset; project={projectCode.Trim()}; page={pageNumber?.ToString() ?? "n/a"}; sha256={sha256}"
        };

        var request = _google.Drive.Files.Create(metadata, buffered, mime);
        request.Fields = "id,name,mimeType,size,parents,webViewLink,createdTime";

        DriveFile? uploaded;
        try
        {
            var progress = await request.UploadAsync(ct);
            uploaded = request.ResponseBody;

            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || uploaded?.Id is null)
            {
                var uploadError = progress.Exception;
                _logger.LogError(uploadError,
                    "Drive upload did not complete. Status={UploadStatus}; Message={Message}",
                    progress.Status,
                    uploadError?.Message);

                return StatusCode(502, new
                {
                    success = false,
                    error = "drive_upload_failed",
                    uploadStatus = progress.Status.ToString(),
                    googleStatus = (uploadError as GoogleApiException)?.HttpStatusCode.ToString(),
                    googleMessage = uploadError?.Message
                });
            }
        }
        catch (GoogleApiException ex)
        {
            _logger.LogError(ex, "Google Drive API rejected asset upload.");
            return StatusCode(502, new
            {
                success = false,
                error = "drive_upload_failed",
                googleStatus = ex.HttpStatusCode.ToString(),
                googleMessage = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Drive asset upload failure.");
            return StatusCode(502, new
            {
                success = false,
                error = "drive_upload_failed",
                exceptionType = ex.GetType().Name,
                message = ex.Message
            });
        }

        // Read back from Drive: an asset only counts after this verification.
        var verifyRequest = _google.Drive.Files.Get(uploaded.Id);
        verifyRequest.Fields = "id,name,mimeType,size,parents,webViewLink,trashed";
        var verified = await verifyRequest.ExecuteAsync(ct);

        var verifiedOk = verified.Id == uploaded.Id
            && verified.Trashed != true
            && verified.Parents?.Contains(generatedFolderId) == true
            && verified.Size.HasValue
            && verified.Size.Value == file.Length;

        if (!verifiedOk)
        {
            _logger.LogError("Drive read-back verification failed for {DriveFileId}.", uploaded.Id);
            return StatusCode(502, new { success = false, error = "drive_verification_failed", driveFileId = uploaded.Id });
        }

        await RegisterAssetAsync(projectCode.Trim(), pageNumber, verified, sha256, file.Length, ct);

        return Ok(new
        {
            success = true,
            verified = true,
            projectId = projectCode.Trim(),
            pageNumber,
            fileName = verified.Name,
            driveFileId = verified.Id,
            driveUrl = verified.WebViewLink ?? $"https://drive.google.com/file/d/{verified.Id}/view",
            folderId = generatedFolderId,
            mimeType = verified.MimeType,
            size = verified.Size!.Value,
            sha256
        });
    }

    [HttpGet("{driveFileId}/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(string driveFileId, CancellationToken ct)
    {
        if (!IsAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        var request = _google.Drive.Files.Get(driveFileId);
        request.Fields = "id,name,mimeType,size,parents,webViewLink,trashed";
        var file = await request.ExecuteAsync(ct);
        return Ok(new
        {
            success = true,
            verified = file.Trashed != true,
            driveFileId = file.Id,
            fileName = file.Name,
            mimeType = file.MimeType,
            size = file.Size,
            driveUrl = file.WebViewLink
        });
    }

    private async Task<DriveFile?> FindExistingFileAsync(string parentId, string fileName, CancellationToken ct)
    {
        var list = _google.Drive.Files.List();
        list.Q = $"'{Escape(parentId)}' in parents and name='{Escape(fileName)}' and trashed=false";
        list.PageSize = 1;
        list.Fields = "files(id,name)";
        return (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
    }

    private async Task RegisterAssetAsync(string projectCode, int? pageNumber, DriveFile file, string sha256, long size, CancellationToken ct)
    {
        var project = await _db.Projects.GetByCodeAsync(projectCode, ct);
        if (project is null)
        {
            _logger.LogWarning("Drive image {DriveFileId} verified, but project {ProjectCode} is not present in PostgreSQL; Assets registration skipped.", file.Id, projectCode);
            return;
        }

        var assetCode = pageNumber is > 0 ? $"{projectCode}-IMG-{pageNumber:000}" : $"{projectCode}-IMG-{file.Id}";
        var asset = await _db.Assets.GetByCodeAsync(assetCode, ct);
        if (asset is null)
        {
            asset = new Asset
            {
                AssetCode = assetCode,
                ProjectId = project.Id,
                AssetType = AssetType.Illustration,
                Version = "v1.0",
                Status = AssetStatus.Draft,
                QaStatus = AssetQaStatus.NotChecked,
                DriveFileId = file.Id,
                DriveUrl = file.WebViewLink ?? $"https://drive.google.com/file/d/{file.Id}/view",
                ContentJson = JsonSerializer.Serialize(new { pageNumber, sha256, size, mimeType = file.MimeType, verified = true }),
                Notes = "Verified physical image asset uploaded through Zunavio gateway.",
                CreatedAt = DateTime.UtcNow
            };
            await _db.Assets.AddAsync(asset, ct);
        }
        else
        {
            asset.DriveFileId = file.Id;
            asset.DriveUrl = file.WebViewLink ?? $"https://drive.google.com/file/d/{file.Id}/view";
            asset.ContentJson = JsonSerializer.Serialize(new { pageNumber, sha256, size, mimeType = file.MimeType, verified = true });
            asset.Status = AssetStatus.Draft;
            asset.QaStatus = AssetQaStatus.NotChecked;
        }

        await _db.SaveChangesAsync(ct);
        await _controlCenter.SyncProjectAsync(project.Id, ct);
    }

    private bool IsAuthorized()
    {
        var expected = _configuration["ASSET_UPLOAD_API_KEY"];
        if (string.IsNullOrWhiteSpace(expected))
            return false;

        var supplied = Request.Headers[ApiKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(supplied))
            return false;

        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private async Task<string> EnsureFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var drive = _google.Drive;
        var list = drive.Files.List();
        list.Q = $"'{Escape(parentId)}' in parents and mimeType='application/vnd.google-apps.folder' and name='{Escape(name)}' and trashed=false";
        list.PageSize = 1;
        list.Fields = "files(id)";
        var existing = await list.ExecuteAsync(ct);
        if (existing.Files is { Count: > 0 })
            return existing.Files[0].Id;

        var created = await drive.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = [parentId]
        }).ExecuteAsync(ct);

        return created.Id ?? throw new InvalidOperationException("Could not create GENERATED folder.");
    }

    private static string BuildFileName(string projectCode, int? page, string originalName, string? mime)
    {
        var ext = mime switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
        var suffix = page is > 0 ? $"PAGE_{page:000}" : Path.GetFileNameWithoutExtension(originalName);
        return $"{projectCode}_{suffix}{ext}";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
