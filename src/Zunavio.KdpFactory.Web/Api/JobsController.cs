using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Zunavio.KdpFactory.Application;

namespace Zunavio.KdpFactory.Web.Api;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class JobsController : ControllerBase
{
    private readonly IJobQueryService _jobs;

    public JobsController(IJobQueryService jobs) => _jobs = jobs;

    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await _jobs.GetRecentAsync(Math.Clamp(take, 1, 500), ct));
}