using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Application.HumanReview;

namespace Zunavio.KdpFactory.Web.Api;

public sealed record ResolutionRequest
{
    public string? Comment { get; init; }
    public string? ResolutionPayloadJson { get; init; }
}

[ApiController]
[Route("api/[controller]")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IHumanReviewService _reviewsService;
    private readonly IReviewQueryService _queries;

    public ReviewsController(IHumanReviewService reviewsService, IReviewQueryService queries)
    {
        _reviewsService = reviewsService;
        _queries = queries;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending(CancellationToken ct) => Ok(await _queries.GetPendingAsync(ct));

    [HttpGet]
    public async Task<IActionResult> GetByProject([FromQuery] Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest(new { error = "projectId is required." });
        return Ok(await _queries.GetByProjectAsync(projectId, ct));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ResolutionRequest dto, CancellationToken ct)
    {
        var result = await _reviewsService.ApproveAsync(id, dto?.Comment, dto?.ResolutionPayloadJson, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ResolutionRequest dto, CancellationToken ct)
    {
        var result = await _reviewsService.RejectAsync(id, dto?.Comment, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] ResolutionRequest dto, CancellationToken ct)
    {
        var result = await _reviewsService.CancelAsync(id, dto?.Comment, ct);
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }
}