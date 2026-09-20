using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Zunavio.KdpFactory.Domain.Entities;

namespace Zunavio.KdpFactory.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.ProjectCode).HasMaxLength(16).IsRequired();
        builder.HasIndex(p => p.ProjectCode).IsUnique();

        builder.Property(p => p.WorkingTitle).HasMaxLength(200).IsRequired();
        builder.Property(p => p.FinalTitle).HasMaxLength(200);
        builder.Property(p => p.Marketplace).HasMaxLength(64);
        builder.Property(p => p.Language).HasMaxLength(32);
        builder.Property(p => p.TargetAge).HasMaxLength(32);
        builder.Property(p => p.BookType).HasMaxLength(64);
        builder.Property(p => p.Season).HasMaxLength(64);

        builder.Property(p => p.CurrentGate).HasConversion<int>();
        builder.Property(p => p.Status).HasConversion<int>();

        builder.Property(p => p.MarketScore);
        builder.Property(p => p.CurrentManuscriptVersion).HasMaxLength(16);
        builder.Property(p => p.CurrentVisualBibleVersion).HasMaxLength(16);
        builder.Property(p => p.CurrentProductionVersion).HasMaxLength(16);
        builder.Property(p => p.QaResult).HasColumnType("text");
        builder.Property(p => p.NextAction).HasMaxLength(64);
        builder.Property(p => p.SelectedConceptJson).HasColumnType("text");
        builder.Property(p => p.DriveFolderId).HasMaxLength(255);
        builder.Property(p => p.DriveFolderUrl).HasMaxLength(2048);
        builder.Property(p => p.ExternalId).HasMaxLength(128);
        builder.HasIndex(p => p.ExternalId);

        builder.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");

        builder.Property(p => p.RowVersion).HasColumnName("row_version").HasDefaultValueSql("0").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        builder.HasMany(p => p.Runs).WithOne(r => r.Project).HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Assets).WithOne(a => a.Project).HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Reviews).WithOne(r => r.Project).HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Events).WithOne(e => e.Project).HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AgentDefinitionConfiguration : IEntityTypeConfiguration<AgentDefinition>
{
    public void Configure(EntityTypeBuilder<AgentDefinition> builder)
    {
        builder.ToTable("agent_definitions");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(a => a.Code).IsUnique();

        builder.Property(a => a.Name).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.PromptDriveFileId).HasMaxLength(512);
        builder.Property(a => a.PromptDriveUrl).HasMaxLength(2048);
        builder.Property(a => a.PromptVersion).HasMaxLength(32);
        builder.Property(a => a.PromptHash).HasMaxLength(64);
        builder.Property(a => a.PromptTextCache).HasColumnType("text");

        builder.Property(a => a.Enabled).HasDefaultValue(true);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(a => a.UpdatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RunCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.RunCode).IsUnique();

        builder.Property(r => r.InputVersion).HasMaxLength(32);
        builder.Property(r => r.OutputVersion).HasMaxLength(32);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.PromptSnapshot).HasColumnType("text").IsRequired();
        builder.Property(r => r.PromptVersion).HasMaxLength(32);
        builder.Property(r => r.PromptHash).HasMaxLength(64);
        builder.Property(r => r.InputSnapshotJson).HasColumnType("text");
        builder.Property(r => r.OutputJson).HasColumnType("text");
        builder.Property(r => r.Summary).HasMaxLength(4000);
        builder.Property(r => r.BlockingIssuesJson).HasColumnType("text");
        builder.Property(r => r.GateRecommendation).HasMaxLength(64);
        builder.Property(r => r.Model).HasMaxLength(128);
        builder.Property(r => r.OpenAiRequestId).HasMaxLength(128);
        builder.Property(r => r.EstimatedCost).HasColumnType("numeric(18,6)");
        builder.Property(r => r.ErrorMessage).HasMaxLength(4000);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(r => r.IdempotencyKey);

        builder.Property(r => r.CreatedAt).HasDefaultValueSql("now()");

        builder.HasOne(r => r.AgentDefinition).WithMany(a => a.Runs).HasForeignKey(r => r.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Assets).WithOne(a => a.CreatedByRun).HasForeignKey(a => a.CreatedByAgentRunId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AssetCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(a => a.AssetCode).IsUnique();
        builder.HasIndex(a => new { a.ProjectId, a.AssetType });

        builder.Property(a => a.AssetType).HasConversion<int>();
        builder.Property(a => a.Version).HasMaxLength(16).IsRequired();
        builder.Property(a => a.DriveFileId).HasMaxLength(512);
        builder.Property(a => a.DriveUrl).HasMaxLength(2048);
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.QaStatus).HasConversion<int>();
        builder.Property(a => a.ContentJson).HasColumnType("text");
        builder.Property(a => a.Notes).HasMaxLength(4000);

        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
    }
}

public sealed class HumanReviewConfiguration : IEntityTypeConfiguration<HumanReviewRequest>
{
    public void Configure(EntityTypeBuilder<HumanReviewRequest> builder)
    {
        builder.ToTable("human_review_requests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReviewType).HasConversion<int>();
        builder.Property(r => r.Title).HasMaxLength(255).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(4000);
        builder.Property(r => r.PayloadJson).HasColumnType("text");
        builder.Property(r => r.ResolutionPayloadJson).HasMaxLength(2048);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ResolutionComment).HasMaxLength(4000);

        builder.HasIndex(r => new { r.ProjectId, r.Status });
        builder.Property(r => r.RequestedAt).HasDefaultValueSql("now()");

        builder.HasOne(r => r.AgentRun).WithMany().HasForeignKey(r => r.AgentRunId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.ToTable("workflow_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<int>();
        builder.Property(e => e.OldGate).HasConversion<int?>();
        builder.Property(e => e.NewGate).HasConversion<int?>();
        builder.Property(e => e.Title).HasMaxLength(255);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.PayloadJson).HasColumnType("text");

        builder.HasIndex(e => new { e.ProjectId, e.CreatedAt });
    }
}

public sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("background_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.JobType).HasMaxLength(32).IsRequired();
        builder.Property(j => j.PayloadJson).HasColumnType("text");
        builder.Property(j => j.Status).HasConversion<int>();
        builder.Property(j => j.LastError).HasMaxLength(4000);
        builder.Property(j => j.IdempotencyKey).HasMaxLength(255);

        builder.HasIndex(j => new { j.Status, j.AvailableAt });
        builder.HasIndex(j => j.IdempotencyKey);
    }
}