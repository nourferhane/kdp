using Zunavio.KdpFactory.Application.Services;
using Zunavio.KdpFactory.Domain.Entities;
using Zunavio.KdpFactory.Domain.Enums;

namespace Zunavio.KdpFactory.Application.Tests;

public class ProjectQueryServiceTests
{
    private static Project NewProject(string code, ProjectStatus status = ProjectStatus.Active,
        ProjectGate gate = ProjectGate.MarketResearch) => new()
    {
        Id = Guid.NewGuid(),
        ProjectCode = code,
        WorkingTitle = $"{code} title",
        CurrentGate = gate,
        Status = status,
        UpdatedAt = DateTime.UtcNow.AddMinutes(-1),
    };

    [Fact]
    public async Task GetAllAsync_orders_by_most_recently_updated_first()
    {
        var db = new FakeUnitOfWork();
        var older = NewProject("ZNV-001");
        var newer = NewProject("ZNV-002");
        older.UpdatedAt = DateTime.UtcNow.AddDays(-2);
        db.Projects.Projects.AddRange(older, newer);

        var summary = (await new ProjectQueryService(db).GetAllAsync(CancellationToken.None)).ToList();

        Assert.True(summary[0].UpdatedAt >= summary[1].UpdatedAt);
        Assert.Equal("ZNV-002", summary[0].ProjectCode);
    }

    [Fact]
    public async Task GetAllAsync_counts_pending_reviews_and_marks_running_jobs()
    {
        var db = new FakeUnitOfWork();
        var project = NewProject("ZNV-001");
        db.Projects.Projects.Add(project);
        db.Reviews.Requests.Add(new HumanReviewRequest { ProjectId = project.Id, Title = "Concept selection", Status = HumanReviewStatus.Pending });
        db.Reviews.Requests.Add(new HumanReviewRequest { ProjectId = project.Id, Title = "Cover approval", Status = HumanReviewStatus.Pending });
        db.Runs.Runs.Add(new AgentRun { ProjectId = project.Id, RunCode = "RUN-001", Status = AgentRunStatus.Running });

        var summary = Assert.Single(await new ProjectQueryService(db).GetAllAsync(CancellationToken.None));

        Assert.Equal(2, summary.PendingReviews);
        Assert.True(summary.HasRunningJobs);
        Assert.Equal("Waiting for human review: Concept selection; Cover approval", summary.BlockedReason);
        Assert.True(summary.IsBlocked);
    }

    [Fact]
    public async Task GetAllAsync_condenses_many_pending_reviews_into_a_suffix()
    {
        var db = new FakeUnitOfWork();
        var project = NewProject("ZNV-001");
        db.Projects.Projects.Add(project);
        for (var i = 0; i < 5; i++)
        {
            db.Reviews.Requests.Add(new HumanReviewRequest { ProjectId = project.Id, Title = $"Review {i}", Status = HumanReviewStatus.Pending });
        }

        var summary = Assert.Single(await new ProjectQueryService(db).GetAllAsync(CancellationToken.None));

        Assert.Equal("Waiting for human review: Review 0; Review 1 (+3 more)", summary.BlockedReason);
    }

    [Theory]
    [InlineData(ProjectStatus.Paused, "Paused (on hold)")]
    [InlineData(ProjectStatus.Rejected, "Rejected")]
    public async Task GetAllAsync_explains_terminal_states(ProjectStatus status, string expected)
    {
        var db = new FakeUnitOfWork();
        db.Projects.Projects.Add(NewProject("ZNV-001", status));

        var summary = Assert.Single(await new ProjectQueryService(db).GetAllAsync(CancellationToken.None));

        Assert.Equal(expected, summary.BlockedReason);
        Assert.True(summary.IsBlocked);
    }

    [Fact]
    public async Task GetAllAsync_active_idle_project_is_not_blocked()
    {
        var db = new FakeUnitOfWork();
        db.Projects.Projects.Add(NewProject("ZNV-001"));

        var summary = Assert.Single(await new ProjectQueryService(db).GetAllAsync(CancellationToken.None));

        Assert.Null(summary.BlockedReason);
        Assert.False(summary.IsBlocked);
        Assert.False(summary.HasRunningJobs);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_unknown_project()
    {
        var result = await new ProjectQueryService(new FakeUnitOfWork())
            .GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_includes_runs_and_assets_with_agent_names()
    {
        var db = new FakeUnitOfWork();
        var project = NewProject("ZNV-001");
        var writer = new AgentDefinition { Id = Guid.NewGuid(), Code = "WRITER", Name = "Writer Agent" };
        db.Projects.Projects.Add(project);
        db.Agents.Agents.Add(writer);
        db.Runs.Runs.Add(new AgentRun
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            RunCode = "RUN-001",
            AgentDefinitionId = writer.Id,
            Status = AgentRunStatus.Complete,
            Summary = "Drafted chapter one",
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
        });
        db.Assets.Assets.Add(new Asset
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            AssetCode = "AST-001",
            AssetType = AssetType.Manuscript,
            Version = "v0.1",
            Status = AssetStatus.Draft,
            QaStatus = AssetQaStatus.NotChecked,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
        });

        var detail = await new ProjectQueryService(db).GetAsync(project.Id, CancellationToken.None);

        Assert.NotNull(detail);
        var run = Assert.Single(detail!.AgentRuns);
        Assert.Equal("RUN-001", run.RunCode);
        Assert.Equal("WRITER", run.AgentCode);
        Assert.Equal("Writer Agent", run.AgentName);
        Assert.Equal("Drafted chapter one", run.Summary);

        var asset = Assert.Single(detail.Assets);
        Assert.Equal("AST-001", asset.AssetCode);
        Assert.Equal(AssetType.Manuscript, asset.AssetType);
        Assert.Equal(AssetQaStatus.NotChecked, asset.QaStatus);
        Assert.Equal("v0.1", asset.Version);
    }
}