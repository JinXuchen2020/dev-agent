using AgentPlatform.Domain.Aggregates.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 配置 for <see cref="ConversationWorkflowBinding"/>（ITenantScoped，Chat 触发器多对多绑定表）。
/// 租户隔离由 AppDbContext 全局查询过滤器强制。
/// </summary>
internal sealed class ConversationWorkflowBindingConfiguration : IEntityTypeConfiguration<ConversationWorkflowBinding>
{
    public void Configure(EntityTypeBuilder<ConversationWorkflowBinding> builder)
    {
        builder.ToTable("ConversationWorkflowBindings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ConversationId).IsRequired();
        builder.Property(x => x.WorkflowId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ConversationId });
        builder.HasIndex(x => new { x.TenantId, x.WorkflowId });
    }
}
