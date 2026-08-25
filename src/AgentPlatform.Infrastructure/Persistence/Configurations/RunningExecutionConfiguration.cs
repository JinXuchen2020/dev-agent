using AgentPlatform.Domain.Aggregates.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="RunningExecution"/> aggregate root.
/// Maps the in-flight execution state used for durable scheduling and crash recovery (F30).
/// </summary>
internal sealed class RunningExecutionConfiguration : IEntityTypeConfiguration<RunningExecution>
{
    public void Configure(EntityTypeBuilder<RunningExecution> builder)
    {
        builder.ToTable("RunningExecutions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // 1:1 with WorkflowId

        builder.Property(x => x.WorkflowId)
            .IsRequired();

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.WorkflowState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.HeartbeatAt)
            .IsRequired();

        builder.Property(x => x.LeaseExpiresAt)
            .IsRequired();

        builder.Property(x => x.InstanceId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.CheckpointVersion)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.BlackboardSnapshot)
            .HasColumnType("TEXT");

        // Query filter for tenant isolation is applied in AppDbContext.OnModelCreating via ITenantScoped

        // Indexes for scheduler queries
        builder.HasIndex(x => new { x.TenantId, x.WorkflowState, x.LeaseExpiresAt });
        builder.HasIndex(x => x.WorkflowId).IsUnique();
    }
}