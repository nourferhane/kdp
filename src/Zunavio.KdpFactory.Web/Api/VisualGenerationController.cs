using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Web.Api;

[ApiController]
[Route("api/visuals")]
public sealed class VisualGenerationController(
    IVisualGenerationService visuals,
    IConfiguration configuration) : ControllerBase
{
    private const string ApiKeyHeader = "X-Asset-Api-Key";

    public sealed record GenerateVisualBody(
        string ProjectCode,
        int PageNumber,
        string Prompt,
        string? Size,
        string? Quality);

    [HttpPost("generate")]
    [AllowAnonymous]
    public async Task<IActionResult> Generate([FromBody] GenerateVisualBody body, CancellationToken ct)
    {
        if (!IsAuthorized())
            return Unauthorized(new { success = false, error = "invalid_api_key" });

        try
        {
            var result = await visuals.GenerateAndPersistAsync(new VisualGenerationRequest
            {
                ProjectCode = body.ProjectCode,
                PageNumber = body.PageNumber,
                Prompt = body.Prompt,
                Size = body.Size,
                Quality = body.Quality
            }, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = "invalid_request", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(502, new { success = false, error = "generation_or_persistence_failed", message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, error = "image_provider_failed", message = ex.Message });
        }
    }

    private bool IsAuthorized()
    {
        var expected = configuration["ASSET_UPLOAD_API_KEY"];
        var supplied = Request.Headers[ApiKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
            return false;

        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(supplied);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
