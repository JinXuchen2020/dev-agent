using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 配置 for <see cref="WorkflowTrigger"/>（ITenantScoped）。租户隔离由 AppDbContext
/// 全局查询过滤器强制；此处仅定义表结构、列约束与索引（不含过滤器）。
/// </summary>
internal sealed class WorkflowTriggerConfiguration : IEntityTypeConfiguration<WorkflowTrigger>
{
    public void Configure(EntityTypeBuilder<WorkflowTrigger> builder)
    {
        builder.ToTable("WorkflowTriggers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.WorkflowId).IsRequired();
        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.TriggerToken).HasMaxLength(64);
        builder.Property(x => x.Cron).HasMaxLength(100);
        builder.Property(x => x.Timezone).HasMaxLength(100);
        builder.Property(x => x.Enabled).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // 每个工作流每种类型至多一个触发器。
        builder.HasIndex(x => new { x.TenantId, x.WorkflowId, x.Type })
            .IsUnique();
        // 匿名 Webhook 端点按 token 查找。
        builder.HasIndex(x => x.TriggerToken);
        // 调度器扫描到期项。
        builder.HasIndex(x => x.NextRunAt);
    }
}
