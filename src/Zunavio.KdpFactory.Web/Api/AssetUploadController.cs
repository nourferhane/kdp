using System.Security.Cryptography;
using Google.Apis.Drive.v3;
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
    private readonly ILogger<AssetUploadController> _logger;

    public AssetUploadController(
        IArtifactStorage storage,
        IGoogleCredentialProvider google,
        IConfiguration configuration,
        ILogger<AssetUploadController> logger)
    {
        _storage = storage;
        _google = google;
        _configuration = configuration;
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

        var project = await _storage.EnsureProjectFoldersAsync(projectCode.Trim(), ct);
        if (!project.SubFolders.TryGetValue("05_VISUALS", out var visualsFolderId))
            return StatusCode(500, new { success = false, error = "visuals_folder_missing" });

        var generatedFolderId = await EnsureFolderAsync(visualsFolderId, "GENERATED", ct);
        var safeName = BuildFileName(projectCode.Trim(), pageNumber, file.FileName, mime);

        await using var input = file.OpenReadStream();
        await using var buffered = new MemoryStream();
        await input.CopyToAsync(buffered, ct);
        var bytes = buffered.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        buffered.Position = 0;

        var metadata = new DriveFile
        {
            Name = safeName,
            Parents = [generatedFolderId],
            MimeType = mime,
            Description = $"Zunavio asset; project={projectCode.Trim()}; page={pageNumber?.ToString() ?? "n/a"}; sha256={sha256}"
        };

        var request = _google.Drive.Files.Create(metadata, buffered, mime);
        request.Fields = "id,name,mimeType,size,parents,webViewLink,createdTime";
        await request.UploadAsync(ct);
        var uploaded = request.ResponseBody;

        if (uploaded?.Id is null)
            return StatusCode(502, new { success = false, error = "drive_upload_failed" });

        // Read back from Drive: an asset only counts after this verification.
        var verifyRequest = _google.Drive.Files.Get(uploaded.Id);
        verifyRequest.Fields = "id,name,mimeType,size,parents,webViewLink,trashed";
        var verified = await verifyRequest.ExecuteAsync(ct);

        var verifiedOk = verified.Id == uploaded.Id
            && verified.Trashed != true
            && verified.Parents?.Contains(generatedFolderId) == true
            && long.TryParse(verified.Size, out var driveSize)
            && driveSize == file.Length;

        if (!verifiedOk)
        {
            _logger.LogError("Drive read-back verification failed for {DriveFileId}.", uploaded.Id);
            return StatusCode(502, new { success = false, error = "drive_verification_failed", driveFileId = uploaded.Id });
        }

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
            size = driveSize,
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
