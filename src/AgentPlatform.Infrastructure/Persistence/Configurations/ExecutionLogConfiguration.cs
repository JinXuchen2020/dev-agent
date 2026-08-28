using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="ExecutionLog"/> aggregate root.
/// Maps the aggregate and its owned collection of <see cref="ExecutionLogEntry"/> items.
/// </summary>
internal sealed class ExecutionLogConfiguration : IEntityTypeConfiguration<ExecutionLog>
{
    public void Configure(EntityTypeBuilder<ExecutionLog> builder)
    {
        builder.ToTable("ExecutionLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.WorkflowId)
            .IsRequired();

        builder.Property(x => x.WorkflowName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.TotalSteps)
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        // F30 Durable Execution: Checkpoint fields
        builder.Property(x => x.CheckpointData)
            .HasColumnType("TEXT");

        builder.Property(x => x.CheckpointVersion)
            .IsRequired()
            .HasDefaultValue(0);

        // OwnsMany for ExecutionLogEntry collection
        builder.Navigation(x => x.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(x => x.Entries, entryBuilder =>
        {
            entryBuilder.ToTable("ExecutionLogEntries");
            entryBuilder.WithOwner().HasForeignKey("ExecutionLogId");

            entryBuilder.HasKey(x => x.Id);

            entryBuilder.Property(x => x.Id)
                .ValueGeneratedNever();

            entryBuilder.Property(x => x.StepName)
                .IsRequired()
                .HasMaxLength(200);

            entryBuilder.Property(x => x.StepOrder)
                .IsRequired();

            entryBuilder.Property(x => x.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            entryBuilder.Property(x => x.Duration)
                .IsRequired();

            // 模型输出可能很长（真实 LLM 回复常超 4k），改用无长度上限的 text，
            // 避免 StepCompleted 落库时 String or binary data would be truncated → 500。
            entryBuilder.Property(x => x.Result)
                .HasColumnType("text");

            entryBuilder.Property(x => x.ErrorDetail)
                .HasMaxLength(2000);

            // F24 trace: token accounting + node type
            entryBuilder.Property(x => x.TokensIn)
                .IsRequired();

            entryBuilder.Property(x => x.TokensOut)
                .IsRequired();

            entryBuilder.Property(x => x.NodeType)
                .HasConversion<int?>();

            entryBuilder.Property(x => x.StartedAt)
                .IsRequired();

            entryBuilder.Property(x => x.CompletedAt)
                .IsRequired();
        });

        builder.HasIndex(x => x.WorkflowId);
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.StartedAt);
    }
}
