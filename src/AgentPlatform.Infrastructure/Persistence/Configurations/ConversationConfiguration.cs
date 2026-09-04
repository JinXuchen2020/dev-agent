using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Conversation"/> aggregate.
/// </summary>
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    /// <summary>
    /// Configures the entity type mapping for conversations, including table, primary key, tenant scoping, status conversion, owned token usage, and the owned messages collection.
    /// </summary>
    /// <param name="builder">The entity type builder used to configure the mapping.</param>
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(c => c.KnowledgeBaseId).IsRequired(false);
        builder.Property(c => c.CollectionName)
            .IsRequired(false)
            .HasMaxLength(120);
        // F36：agent 归属（nullable = 人工创建/Chat 绑定的全局会话，存量兼容）。
        builder.Property(c => c.AgentId).IsRequired(false);
        // F36 审查修复：per-agent 会话唯一性由数据库强制——并发的同 (tenant, workflow, agent)
        // 双步骤同时创建会话时，后者提交失败并被 best-effort 包裹吞掉（仅告警），
        // 杜绝 GetByAgentAsync 命中双行导致历史分裂。过滤掉存量 AgentId=NULL 行。
        // （复合索引同时覆盖 GetByAgentAsync 的查询谓词；不另建单列 AgentId 索引。）
        builder.HasIndex(c => new { c.TenantId, c.WorkflowId, c.AgentId })
            .IsUnique()
            .HasFilter("\"AgentId\" IS NOT NULL");
        builder.OwnsOne(c => c.TotalTokenUsage, t =>
        {
            t.Property(p => p.PromptTokens).HasColumnName("PromptTokens");
            t.Property(p => p.CompletionTokens).HasColumnName("CompletionTokens");
        });

        builder.OwnsMany(c => c.Messages, msg =>
        {
            msg.WithOwner().HasForeignKey("ConversationId");
            // 关键：Message 的主键 Id 由代码显式赋 Guid.NewGuid()，必须 ValueGeneratedNever，
            // 否则 EF 误判为"已存在实体"而生成 UPDATE 而非 INSERT，导致 DbUpdateConcurrencyException
            // （expected 1 row, affected 0）。与 KnowledgeDocument 的修复同因。
            msg.Property<Guid>("Id").ValueGeneratedNever();
            msg.HasKey("Id");
            msg.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(50);
            msg.Property(m => m.Content).IsRequired().HasMaxLength(16000);
            msg.Property(m => m.ToolCalls).HasMaxLength(8000);
            msg.OwnsOne(m => m.TokenUsage, t =>
            {
                t.Property(p => p.PromptTokens).HasColumnName("MsgPromptTokens");
                t.Property(p => p.CompletionTokens).HasColumnName("MsgCompletionTokens");
            });
        });

        builder.Navigation(c => c.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
