using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zunavio.KdpFactory.Application.Abstractions;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;
using Zunavio.KdpFactory.Infrastructure.Configuration;
using Zunavio.KdpFactory.Infrastructure.Google;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Zunavio.KdpFactory.Infrastructure.OpenAi;

public sealed class OpenAiVisualGenerationService(
    HttpClient http,
    IOptions<OpenAiOptions> openAiOptions,
    IConfiguration configuration,
    IArtifactStorage storage,
    IGoogleCredentialProvider google,
    IUnitOfWork db,
    IGoogleControlCenterSyncService controlCenter,
    ILogger<OpenAiVisualGenerationService> logger) : IVisualGenerationService
{
    private const int MaxImageBytes = 25 * 1024 * 1024;

    public async Task<VisualGenerationResult> GenerateAndPersistAsync(VisualGenerationRequest request, CancellationToken ct)
    {
        var projectCode = request.ProjectCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectCode))
            throw new ArgumentException("ProjectCode is required.", nameof(request));
        if (request.PageNumber <= 0 || request.PageNumber > 500)
            throw new ArgumentOutOfRangeException(nameof(request.PageNumber), "PageNumber must be between 1 and 500.");
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt is required.", nameof(request));
        if (request.Prompt.Length > 32000)
            throw new ArgumentException("Prompt exceeds the image API limit.", nameof(request));

        var projectEntity = await db.Projects.GetByCodeAsync(projectCode, ct)
            ?? throw new InvalidOperationException($"Project '{projectCode}' does not exist in PostgreSQL.");

        var assetCode = $"{projectCode}-IMG-{request.PageNumber:000}";
        var existingAsset = await db.Assets.GetByCodeAsync(assetCode, ct);
        if (existingAsset is not null
            && !string.IsNullOrWhiteSpace(existingAsset.DriveFileId)
            && await IsDriveFileVerifiedAsync(existingAsset.DriveFileId, ct))
        {
            var existingMeta = ParseContent(existingAsset.ContentJson);
            return new VisualGenerationResult
            {
                Success = true,
                Verified = true,
                Idempotent = true,
                ProjectCode = projectCode,
                PageNumber = request.PageNumber,
                AssetCode = assetCode,
                DriveFileId = existingAsset.DriveFileId!,
                DriveUrl = existingAsset.DriveUrl ?? $"https://drive.google.com/file/d/{existingAsset.DriveFileId}/view",
                FileName = $"{projectCode}_PAGE_{request.PageNumber:000}.png",
                MimeType = existingMeta.mimeType ?? "image/png",
                SizeBytes = existingMeta.size,
                Sha256 = existingMeta.sha256 ?? string.Empty,
                Model = existingMeta.model ?? "existing",
                RevisedPrompt = null
            };
        }

        var apiKey = openAiOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"OpenAI image generation requires '{KdpSettings.OpenAiApiKeyEnvKey}'.");

        var model = configuration["OPENAI_IMAGE_MODEL"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-image-2";

        var size = string.IsNullOrWhiteSpace(request.Size) ? "1024x1536" : request.Size.Trim();
        var quality = string.IsNullOrWhiteSpace(request.Quality) ? "medium" : request.Quality.Trim().ToLowerInvariant();

        using var message = new HttpRequestMessage(HttpMethod.Post, BuildImagesEndpoint(openAiOptions.Value.BaseUrl));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            prompt = request.Prompt.Trim(),
            size,
            quality,
            output_format = "png",
            n = 1
        }), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI image generation failed ({(int)response.StatusCode}): {Truncate(json, 1200)}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("b64_json", out var b64)
            || string.IsNullOrWhiteSpace(b64.GetString()))
        {
            throw new InvalidOperationException("OpenAI image response did not contain data[0].b64_json.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(b64.GetString()!);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("OpenAI returned invalid base64 image data.", ex);
        }

        if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
            throw new InvalidOperationException($"Generated image size {bytes.Length} is outside the accepted range.");

        byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < pngSignature.Length || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
            throw new InvalidOperationException("Generated image payload is not a valid PNG stream.");

        var revisedPrompt = data[0].TryGetProperty("revised_prompt", out var revised) ? revised.GetString() : null;
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var projectFolders = await storage.EnsureProjectFoldersAsync(projectCode, ct);
        if (!projectFolders.SubFolders.TryGetValue("05_VISUALS", out var visualsFolderId))
            throw new InvalidOperationException("05_VISUALS folder is missing.");

        var generatedFolderId = await EnsureFolderAsync(visualsFolderId, "GENERATED", ct);
        var fileName = $"{projectCode}_PAGE_{request.PageNumber:000}.png";

        var existingDrive = await FindExistingFileAsync(generatedFolderId, fileName, ct);
        DriveFile saved;
        await using var stream = new MemoryStream(bytes, writable: false);

        if (existingDrive is null)
        {
            var create = google.Drive.Files.Create(new DriveFile
            {
                Name = fileName,
                Parents = [generatedFolderId],
                MimeType = "image/png",
                Description = $"Zunavio generated asset; project={projectCode}; page={request.PageNumber}; sha256={sha256}; model={model}"
            }, stream, "image/png");
            create.Fields = "id,name,mimeType,size,parents,webViewLink,description,trashed";
            var progress = await create.UploadAsync(ct);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || create.ResponseBody?.Id is null)
                throw new InvalidOperationException($"Google Drive upload failed: {progress.Exception?.Message ?? progress.Status.ToString()}");
            saved = create.ResponseBody;
        }
        else
        {
            var update = google.Drive.Files.Update(new DriveFile
            {
                Name = fileName,
                MimeType = "image/png",
                Description = $"Zunavio generated asset; project={projectCode}; page={request.PageNumber}; sha256={sha256}; model={model}"
            }, existingDrive.Id, stream, "image/png");
            update.Fields = "id,name,mimeType,size,parents,webViewLink,description,trashed";
            var progress = await update.UploadAsync(ct);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed || update.ResponseBody?.Id is null)
                throw new InvalidOperationException($"Google Drive update failed: {progress.Exception?.Message ?? progress.Status.ToString()}");
            saved = update.ResponseBody;
        }

        var verifyRequest = google.Drive.Files.Get(saved.Id);
        verifyRequest.Fields = "id,name,mimeType,size,parents,webViewLink,description,trashed";
        var verified = await verifyRequest.ExecuteAsync(ct);
        var verifiedOk = verified.Trashed != true
            && verified.Parents?.Contains(generatedFolderId) == true
            && verified.Size == bytes.LongLength;

        if (!verifiedOk)
            throw new InvalidOperationException($"Drive read-back verification failed for generated image '{saved.Id}'.");

        var contentJson = JsonSerializer.Serialize(new
        {
            pageNumber = request.PageNumber,
            sha256,
            size = bytes.LongLength,
            mimeType = "image/png",
            verified = true,
            model,
            requestedSize = size,
            quality,
            source = "openai_images_api"
        });

        var asset = existingAsset ?? new Asset
        {
            AssetCode = assetCode,
            ProjectId = projectEntity.Id,
            AssetType = AssetType.Illustration,
            Version = "v1.0",
            CreatedAt = DateTime.UtcNow
        };
        asset.DriveFileId = verified.Id;
        asset.DriveUrl = verified.WebViewLink ?? $"https://drive.google.com/file/d/{verified.Id}/view";
        asset.ContentJson = contentJson;
        asset.Status = AssetStatus.Draft;
        asset.QaStatus = AssetQaStatus.NotChecked;
        asset.Notes = "Generated by OpenAI Images API and verified by Drive read-back.";

        if (existingAsset is null)
            await db.Assets.AddAsync(asset, ct);

        await db.SaveChangesAsync(ct);
        await controlCenter.SyncProjectAsync(projectEntity.Id, ct);

        logger.LogInformation(
            "Generated and verified {AssetCode} with {Model}; DriveFileId={DriveFileId}; bytes={Bytes}.",
            assetCode, model, verified.Id, bytes.LongLength);

        return new VisualGenerationResult
        {
            Success = true,
            Verified = true,
            Idempotent = false,
            ProjectCode = projectCode,
            PageNumber = request.PageNumber,
            AssetCode = assetCode,
            DriveFileId = verified.Id,
            DriveUrl = asset.DriveUrl!,
            FileName = verified.Name ?? fileName,
            MimeType = verified.MimeType ?? "image/png",
            SizeBytes = verified.Size ?? bytes.LongLength,
            Sha256 = sha256,
            Model = model,
            RevisedPrompt = revisedPrompt
        };
    }

    private async Task<bool> IsDriveFileVerifiedAsync(string fileId, CancellationToken ct)
    {
        try
        {
            var request = google.Drive.Files.Get(fileId);
            request.Fields = "id,size,trashed";
            var file = await request.ExecuteAsync(ct);
            return file.Trashed != true && file.Size is > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> EnsureFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var existing = await FindFolderAsync(parentId, name, ct);
        if (existing is not null)
            return existing.Id;

        var created = await google.Drive.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = [parentId]
        }).ExecuteAsync(ct);

        return created.Id ?? throw new InvalidOperationException($"Could not create folder '{name}'.");
    }

    private async Task<DriveFile?> FindFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var list = google.Drive.Files.List();
        list.Q = $"'{Escape(parentId)}' in parents and mimeType='application/vnd.google-apps.folder' and name='{Escape(name)}' and trashed=false";
        list.PageSize = 1;
        list.Fields = "files(id,name)";
        return (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
    }

    private async Task<DriveFile?> FindExistingFileAsync(string parentId, string fileName, CancellationToken ct)
    {
        var list = google.Drive.Files.List();
        list.Q = $"'{Escape(parentId)}' in parents and name='{Escape(fileName)}' and trashed=false";
        list.PageSize = 1;
        list.Fields = "files(id,name)";
        return (await list.ExecuteAsync(ct)).Files?.FirstOrDefault();
    }

    private static Uri BuildImagesEndpoint(string? baseUrl)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1/" : baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(root), "images/generations");
    }

    private static (string? sha256, long size, string? mimeType, string? model) ParseContent(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            return (null, 0, null, null);
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var r = doc.RootElement;
            return (
                r.TryGetProperty("sha256", out var s) ? s.GetString() : null,
                r.TryGetProperty("size", out var z) && z.TryGetInt64(out var n) ? n : 0,
                r.TryGetProperty("mimeType", out var m) ? m.GetString() : null,
                r.TryGetProperty("model", out var mo) ? mo.GetString() : null
            );
        }
        catch
        {
            return (null, 0, null, null);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
