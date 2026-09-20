using Zunavio.KdpFactory.Application.Abstractions;

namespace Zunavio.KdpFactory.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implements <see cref="IUnitOfWork"/>. All repositories share the scoped
/// <see cref="KdpDbContext"/>, so multiple writes are committed atomically
/// via one <see cref="SaveChangesAsync"/> call.
/// </summary>
public sealed class KdpUnitOfWork(KdpDbContext db) : IUnitOfWork
{
    private readonly Lazy<IProjectRepository> _projects = new(() => new ProjectRepository(db));
    private readonly Lazy<IAgentDefinitionRepository> _agents = new(() => new AgentDefinitionRepository(db));
    private readonly Lazy<IAgentRunRepository> _runs = new(() => new AgentRunRepository(db));
    private readonly Lazy<IAssetRepository> _assets = new(() => new AssetRepository(db));
    private readonly Lazy<IHumanReviewRepository> _reviews = new(() => new HumanReviewRepository(db));
    private readonly Lazy<IWorkflowEventRepository> _events = new(() => new WorkflowEventRepository(db));
    private readonly Lazy<IBackgroundJobRepository> _jobs = new(() => new BackgroundJobRepository(db));

    public IProjectRepository Projects => _projects.Value;
    public IAgentDefinitionRepository Agents => _agents.Value;
    public IAgentRunRepository Runs => _runs.Value;
    public IAssetRepository Assets => _assets.Value;
    public IHumanReviewRepository Reviews => _reviews.Value;
    public IWorkflowEventRepository Events => _events.Value;
    public IBackgroundJobRepository Jobs => _jobs.Value;

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await action(ct);
        await transaction.CommitAsync(ct);
    }
}