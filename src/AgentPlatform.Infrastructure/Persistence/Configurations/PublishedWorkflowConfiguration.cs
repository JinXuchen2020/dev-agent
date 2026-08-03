using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the <see cref="PublishedWorkflow"/> aggregate.
/// 实体为 <see cref="ITenantScoped"/>，租户隔离由 AppDbContext 的全局查询过滤器强制；
/// 此处定义表结构、列约束与辅助索引（不含 TenantId 过滤器——已在 OnModelCreating 统一注入）。
/// </summary>
internal sealed class PublishedWorkflowConfiguration : IEntityTypeConfiguration<PublishedWorkflow>
{
    public void Configure(EntityTypeBuilder<PublishedWorkflow> builder)
    {
        builder.ToTable("PublishedWorkflows");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // 代码显式赋 Guid，规避 EF 默认 ValueGeneratedOnAdd 导致的 UPDATE 命中 0 行陷阱

        builder.Property(x => x.TenantId)
            .IsRequired();

        builder.Property(x => x.WorkflowId)
            .IsRequired();

        builder.Property(x => x.Slug)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.Mode)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(x => x.ApiKeyId);

        builder.Property(x => x.InputSchemaJson)
            .HasColumnType("TEXT");

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        // 同一租户内 slug 唯一（外部调用地址）；另为按工作流查发布状态建索引。
        builder.HasIndex(x => new { x.TenantId, x.Slug })
            .IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.WorkflowId });
    }
}
