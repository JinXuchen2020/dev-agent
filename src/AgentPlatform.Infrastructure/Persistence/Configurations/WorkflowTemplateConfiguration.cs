using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core <see cref="IEntityTypeConfiguration{TEntity}"/> for the platform-level
/// <see cref="WorkflowTemplate"/> aggregate (F23).
/// <para>
/// 模板为平台级共享资源，<b>不实现</b> <c>ITenantScoped</c> —— 不加租户查询过滤器、不建 TenantId 列
/// （对照 <c>AgentRoleDefinitionConfiguration</c> 的做法）。此处仅定义表结构、列约束与索引。
/// </para>
/// </summary>
internal sealed class WorkflowTemplateConfiguration : IEntityTypeConfiguration<WorkflowTemplate>
{
    public void Configure(EntityTypeBuilder<WorkflowTemplate> builder)
    {
        builder.ToTable("WorkflowTemplates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // 代码显式赋 Guid（种子用固定 Guid 幂等），规避 EF 默认 ValueGeneratedOnAdd 的 UPDATE 命中 0 行陷阱

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Description)
            .HasColumnType("TEXT");

        builder.Property(x => x.SnapshotJson)
            .IsRequired()
            .HasColumnType("TEXT");

        // Tags 以 JSON 文本持久化（聚合层提供只读 Tags 列表，TagsJson 用于关键字搜索）。
        builder.Property(x => x.TagsJson)
            .HasColumnType("TEXT");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.Name);
    }
}
