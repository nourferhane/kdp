using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Infrastructure.Google;

namespace Zunavio.KdpFactory.Web.Api;

[ApiController]
[Route("api/assets")]
public sealed class AssetUploadController(
    VerifiedImageUploadService uploader, IGoogleCredentialProvider google, IConfiguration configuration) : ControllerBase
{
    [HttpPost("upload")]
    [AllowAnonymous]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string projectCode,
        [FromForm] int? pageNumber, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { success = false, error = "invalid_api_key" });
        if (file is null || file.Length == 0 || file.Length > VerifiedImageUploadService.MaxBytes)
            return BadRequest(new { success = false, error = "invalid_file_size" });
        if (string.IsNullOrWhiteSpace(projectCode))
            return BadRequest(new { success = false, error = "project_code_required" });
        if (pageNumber is null)
            return BadRequest(new { success = false, error = "page_number_required" });

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        var result = await uploader.UploadAsync(projectCode, pageNumber.Value,
            file.ContentType?.ToLowerInvariant() ?? string.Empty, buffer.ToArray(), ct);
        if (!result.Success)
            return result.ErrorCode switch
            {
                "project_not_found" => NotFound(result),
                "project_folder_missing" or "project_folder_invalid" or "project_state_changed"
                    or "project_not_active_in_visual_production" or "asset_code_conflict" => Conflict(result),
                "drive_upload_failed" or "drive_update_failed" or "drive_verification_failed" or "drive_content_mismatch" => StatusCode(502, result),
                "database_registration_verification_failed" => StatusCode(500, result),
                _ => BadRequest(result)
            };
        return Ok(new { success = true, verified = true, projectId = projectCode.Trim(), pageNumber,
            assetCode = result.AssetCode, driveFileId = result.DriveFileId, driveUrl = result.DriveUrl,
            folderId = result.FolderId, fileName = result.FileName, mimeType = result.MimeType,
            size = result.Size, sha256 = result.Sha256, idempotent = result.Idempotent });
    }

    [HttpGet("{driveFileId}/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(string driveFileId, CancellationToken ct)
    {
        if (!IsAuthorized()) return Unauthorized(new { success = false, error = "invalid_api_key" });
        var get = google.Drive.Files.Get(driveFileId);
        get.Fields = "id,name,mimeType,size,parents,webViewLink,trashed";
        var file = await get.ExecuteAsync(ct);
        return Ok(new { success = true, verified = file.Trashed != true, driveFileId = file.Id,
            fileName = file.Name, mimeType = file.MimeType, size = file.Size, driveUrl = file.WebViewLink });
    }

    private bool IsAuthorized()
    {
        var expected = configuration["ASSET_UPLOAD_API_KEY"];
        var supplied = Request.Headers["X-Asset-Api-Key"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
