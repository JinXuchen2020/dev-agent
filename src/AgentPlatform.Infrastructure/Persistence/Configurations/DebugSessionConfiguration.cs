using AgentPlatform.Domain.Aggregates.Debug;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="DebugSession"/> aggregate root (F25).
/// </summary>
internal sealed class DebugSessionConfiguration : IEntityTypeConfiguration<DebugSession>
{
    public void Configure(EntityTypeBuilder<DebugSession> builder)
    {
        builder.ToTable("DebugSessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.WorkflowId)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.CurrentStepOrder)
            .IsRequired();

        builder.Property(x => x.VariablesJson)
            .IsRequired()
            // 黑板变量持有各节点输出（真实 LLM 回复常超 8k），改用无长度上限的 text，
            // 避免调试单步落库时 String or binary data would be truncated → 500。
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.WorkflowId });
    }
}
