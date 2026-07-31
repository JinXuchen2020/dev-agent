using AgentPlatform.Domain.Aggregates.HumanApprovals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="HumanApproval"/> aggregate root.
/// 实体为 <see cref="ITenantScoped"/>，租户隔离由 AppDbContext 的全局查询过滤器强制；
/// 此处仅定义表结构、列约束与辅助索引（不含 TenantId 过滤器——已在 OnModelCreating 统一注入）。
/// </summary>
internal sealed class HumanApprovalConfiguration : IEntityTypeConfiguration<HumanApproval>
{
    public void Configure(EntityTypeBuilder<HumanApproval> builder)
    {
        builder.ToTable("HumanApprovals");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.WorkflowId)
            .IsRequired();

        builder.Property(x => x.NodeName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Prompt)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.SubmittedInput)
            .HasMaxLength(4000);

        builder.Property(x => x.ResolvedAt);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.ExecutionId);

        // 定位「某节点当前 Pending 审批」与「某工作流全部审批」的高效查询。
        builder.HasIndex(x => new { x.TenantId, x.WorkflowId, x.NodeName, x.Status });
        builder.HasIndex(x => x.WorkflowId);
    }
}
