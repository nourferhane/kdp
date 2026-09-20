using Microsoft.EntityFrameworkCore;
using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Infrastructure.Persistence;

public sealed class KdpDbContext : DbContext
{
    public KdpDbContext(DbContextOptions<KdpDbContext> options) : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<HumanReviewRequest> HumanReviewRequests => Set<HumanReviewRequest>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KdpDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}