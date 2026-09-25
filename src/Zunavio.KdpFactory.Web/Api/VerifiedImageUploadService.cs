using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Google;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Zunavio.KdpFactory.Web.Api;

/// <summary>Shared REST/MCP gateway. A successful result means the bytes were read back from Drive and registered in PostgreSQL.</summary>
public sealed class VerifiedImageUploadService(
    IUnitOfWork db, IGoogleCredentialProvider google, IGoogleControlCenterSyncService controlCenter)
{
    // Base64 expands bytes by 4/3; stay below ASP.NET Core's default 30 MB request limit.
    public const int MaxBytes = 18 * 1024 * 1024;

    public async Task<ImageUploadResult> UploadAsync(string projectCode, int pageNumber, string mimeType,
        byte[] bytes, CancellationToken ct)
    {
        projectCode = projectCode.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(projectCode, "^ZNV-[0-9]{3,}$"))
            return ImageUploadResult.Error("invalid_project_code");
        if (pageNumber <= 0 || pageNumber > 999)
            return ImageUploadResult.Error("invalid_page_number");
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            return ImageUploadResult.Error("invalid_file_size");

        var signatureMime = DetectMime(bytes);
        if (signatureMime is null || signatureMime != mimeType)
            return ImageUploadResult.Error("invalid_image_signature");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        // Two historical diagnostic placeholders must never be accepted as production illustrations.
        if (sha256 is "55299df0571185ffb58d39e8af480ae6f29b1f6f847a1daf1684c020b7bca90a"
            or "3ef925c01577a0f95e6eae6d6220178cf18735a2e8d42aa7292ab75074673f70")
            return ImageUploadResult.Error("diagnostic_placeholder_not_allowed");

        // Check before creating folders or files: an unknown project must never produce a false success.
        var project = await db.Projects.GetByCodeAsync(projectCode, ct);
        if (project is null)
            return ImageUploadResult.Error("project_not_found");
        if (project.CurrentGate != ProjectGate.VisualProduction || project.Status != ProjectStatus.Active)
            return ImageUploadResult.Error("project_not_active_in_visual_production");
        if (string.IsNullOrWhiteSpace(project.DriveFolderId))
            return ImageUploadResult.Error("project_folder_missing");

        var drive = google.Drive;
        var rootRequest = drive.Files.Get(project.DriveFolderId);
        rootRequest.Fields = "id,mimeType,trashed";
        var root = await rootRequest.ExecuteAsync(ct);
        if (root.Trashed == true || root.MimeType != "application/vnd.google-apps.folder")
            return ImageUploadResult.Error("project_folder_invalid");

        var visuals = await EnsureFolderAsync(root.Id, "05_VISUALS", ct);
        var generated = await EnsureFolderAsync(visuals, "GENERATED", ct);
        var extension = mimeType switch { "image/png" => ".png", "image/jpeg" => ".jpg", _ => ".webp" };
        var name = $"{projectCode}_PAGE_{pageNumber:000}{extension}";
        var description = $"Zunavio asset; project={projectCode}; page={pageNumber}; sha256={sha256}";

        var beforeDriveWrite = await db.Projects.GetByCodeAsync(projectCode, ct);
        if (beforeDriveWrite is null || beforeDriveWrite.Id != project.Id
            || beforeDriveWrite.Status != project.Status || beforeDriveWrite.CurrentGate != project.CurrentGate)
            return ImageUploadResult.Error("project_state_changed");

        var list = drive.Files.List();
        list.Q = $"'{Escape(generated)}' in parents and name='{Escape(name)}' and trashed=false";
        list.PageSize = 1;
        list.Fields = "files(id,description)";
        var oldFile = (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
        DriveFile file;
        var idempotent = oldFile?.Description?.Contains($"sha256={sha256}", StringComparison.OrdinalIgnoreCase) == true;
        if (idempotent)
        {
            file = oldFile!;
        }
        else
        {
            await using var input = new MemoryStream(bytes, writable: false);
            if (oldFile is null)
            {
                var create = drive.Files.Create(new DriveFile
                {
                    Name = name, Parents = [generated], MimeType = mimeType, Description = description
                }, input, mimeType);
                create.Fields = "id";
                var progress = await create.UploadAsync(ct);
                if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || create.ResponseBody?.Id is null)
                    return ImageUploadResult.Error("drive_upload_failed");
                file = create.ResponseBody;
            }
            else
            {
                var update = drive.Files.Update(new DriveFile
                {
                    Name = name, MimeType = mimeType, Description = description
                }, oldFile.Id, input, mimeType);
                update.Fields = "id";
                var progress = await update.UploadAsync(ct);
                if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || update.ResponseBody?.Id is null)
                    return ImageUploadResult.Error("drive_update_failed");
                file = update.ResponseBody;
            }
        }

        var read = drive.Files.Get(file.Id);
        read.Fields = "id,name,mimeType,size,parents,webViewLink,trashed";
        var physical = await read.ExecuteAsync(ct);
        if (physical.Id != file.Id || physical.Trashed == true || physical.MimeType != mimeType
            || physical.Size != bytes.LongLength || physical.Parents?.Contains(generated) != true)
            return ImageUploadResult.Error("drive_verification_failed");

        // Metadata alone is insufficient: compare the actual image bytes from Drive.
        await using var download = new MemoryStream();
        var downloaded = await drive.Files.Get(file.Id).DownloadAsync(download, ct);
        if (downloaded.Status != Google.Apis.Download.DownloadStatus.Completed
            || !SHA256.HashData(download.ToArray()).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
            return ImageUploadResult.Error("drive_content_mismatch");

        // Recheck project identity immediately before the DB write.
        var current = await db.Projects.GetByCodeAsync(projectCode, ct);
        if (current is null || current.Id != project.Id || current.CurrentGate != project.CurrentGate
            || current.Status != project.Status)
            return ImageUploadResult.Error("project_state_changed");

        var assetCode = $"{projectCode}-IMG-{pageNumber:000}";
        var asset = await db.Assets.GetTrackedByCodeAsync(assetCode, ct);
        if (asset is not null && asset.ProjectId != project.Id)
            return ImageUploadResult.Error("asset_code_conflict");
        if (asset is null)
        {
            asset = new Asset { AssetCode = assetCode, ProjectId = project.Id, AssetType = AssetType.Illustration,
                Version = "v1.0", CreatedAt = DateTime.UtcNow };
            await db.Assets.AddAsync(asset, ct);
        }
        asset.DriveFileId = physical.Id;
        asset.DriveUrl = physical.WebViewLink ?? $"https://drive.google.com/file/d/{physical.Id}/view";
        asset.ContentJson = JsonSerializer.Serialize(new { pageNumber, sha256, size = bytes.Length, mimeType, verified = true });
        asset.Status = AssetStatus.Draft;
        asset.QaStatus = AssetQaStatus.NotChecked;
        asset.Notes = "Physical image read back from Drive and hash-verified through Zunavio gateway; pending visual QA.";
        await db.SaveChangesAsync(ct);
        var saved = await db.Assets.GetByCodeAsync(assetCode, ct);
        if (saved?.DriveFileId != physical.Id || saved.ProjectId != project.Id
            || saved.ContentJson is null || !saved.ContentJson.Contains(sha256, StringComparison.OrdinalIgnoreCase))
            return ImageUploadResult.Error("database_registration_verification_failed");
        await controlCenter.SyncProjectAsync(project.Id, ct);
        return new ImageUploadResult(true, null, assetCode, physical.Id, asset.DriveUrl, generated,
            physical.Name, mimeType, bytes.Length, sha256, idempotent);
    }

    private async Task<string> EnsureFolderAsync(string parent, string name, CancellationToken ct)
    {
        var list = google.Drive.Files.List();
        list.Q = $"'{Escape(parent)}' in parents and mimeType='application/vnd.google-apps.folder' and name='{Escape(name)}' and trashed=false";
        list.Fields = "files(id)";
        list.PageSize = 1;
        var found = (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
        if (found?.Id is not null) return found.Id;
        var created = await google.Drive.Files.Create(new DriveFile
        {
            Name = name, Parents = [parent], MimeType = "application/vnd.google-apps.folder"
        }).ExecuteAsync(ct);
        return created.Id ?? throw new InvalidOperationException("Drive folder creation returned no ID.");
    }

    private static string Escape(string input) => input.Replace("\\", "\\\\").Replace("'", "\\'");

    public static string? DetectMime(byte[] bytes)
    {
        if (bytes.Length >= 45 && bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })
            && bytes.AsSpan(bytes.Length - 12, 8).SequenceEqual(new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44 }))
            return "image/png";
        if (bytes.Length >= 16 && bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
            && bytes.AsSpan(bytes.Length - 2).SequenceEqual(new byte[] { 0xff, 0xd9 }))
            return "image/jpeg";
        if (bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) == bytes.Length - 8)
            return "image/webp";
        return null;
    }
}

public sealed record ImageUploadResult(bool Success, string? ErrorCode = null, string? AssetCode = null,
    string? DriveFileId = null, string? DriveUrl = null, string? FolderId = null,
    string? FileName = null, string? MimeType = null, long? Size = null, string? Sha256 = null,
    bool Idempotent = false)
{
    public static ImageUploadResult Error(string code) => new(false, code);
}
