using Microsoft.AspNetCore.Mvc;
using Zunavio.KdpFactory.Application;
using Zunavio.KdpFactory.Application.Orchestration;

namespace Zunavio.KdpFactory.Web.Api;

public sealed record CreateProjectRequestDto
{
    public string WorkingTitle { get; init; } = string.Empty;
    public string Marketplace { get; init; } = "Amazon.com";
    public string Language { get; init; } = "English";
    public string TargetAge { get; init; } = string.Empty;
    public string BookType { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string? ProjectCode { get; init; }
    public string? DriveFolderId { get; init; }
    public string? ExternalId { get; init; }
}

[ApiController]
[Route("api/[controller]")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IOrchestratorService _orchestrator;
    private readonly IProjectQueryService _projects;

    public ProjectsController(IOrchestratorService orchestrator, IProjectQueryService projects)
    {
        _orchestrator = orchestrator;
        _projects = projects;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _projects.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var project = await _projects.GetAsync(id, ct);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequestDto dto, CancellationToken ct)
    {
        var result = await _orchestrator.CreateProjectAsync(new CreateProjectRequest
        {
            WorkingTitle = dto.WorkingTitle,
            Marketplace = dto.Marketplace,
            Language = dto.Language,
            TargetAge = dto.TargetAge,
            BookType = dto.BookType,
            Season = dto.Season,
            ProjectCode = dto.ProjectCode,
            DriveFolderId = dto.DriveFolderId,
            ExternalId = dto.ExternalId,
        }, ct);

        if (!result.Success) return BadRequest(new { error = result.Error });
        return result.ProjectId is null ? BadRequest(new { error = "Project id missing." }) : CreatedAtAction(nameof(Get), new { id = result.ProjectId }, result);
    }

    [HttpPost("{id:guid}/run-next")]
    public Task<IActionResult> RunNext(Guid id, CancellationToken ct) => RunAsync(() => _orchestrator.RunNextAsync(id, ct));

    [HttpPost("{id:guid}/run-agent/{agentCode?}")]
    public Task<IActionResult> RunAgent(Guid id, string? agentCode, CancellationToken ct) =>
        RunAsync(() => _orchestrator.RunAgentAsync(id, agentCode, ct));

    [HttpPost("{id:guid}/continue")]
    public Task<IActionResult> Continue(Guid id, CancellationToken ct) => RunAsync(() => _orchestrator.ContinueAsync(id, ct));

    [HttpPost("{id:guid}/pause")]
    public Task<IActionResult> Pause(Guid id, CancellationToken ct) => RunAsync(() => _orchestrator.PauseAsync(id, ct));

    [HttpPost("{id:guid}/resume")]
    public Task<IActionResult> Resume(Guid id, CancellationToken ct) => RunAsync(() => _orchestrator.ResumeAsync(id, ct));

    private async Task<IActionResult> RunAsync(Func<Task<OrchestratorOperationResult>> operation)
    {
        var result = await operation();
        return result.Success ? Ok(result) : BadRequest(new { error = result.Error });
    }
}