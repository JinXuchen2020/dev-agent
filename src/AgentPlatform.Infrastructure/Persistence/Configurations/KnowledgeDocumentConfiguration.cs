using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// 配置 <see cref="KnowledgeDocument"/> 实体映射。
/// 该类型原作为 KnowledgeBase 的“拥有子实体（OwnsMany）”建模，但因具备独立标识
/// （自有 Id，且被向量存储按 DocumentId 外部引用）改为常规一对多关联实体。
/// </summary>
internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    /// <summary>配置文档实体映射。</summary>
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocument");
        builder.HasKey(d => d.Id);
        // 关键修复：Id 由领域层在 Create() 中客户端生成（Guid.NewGuid()）。
        // 若不显式设为 ValueGeneratedNever，EF 对名为 Id 的 Guid 主键默认施加
        // ValueGeneratedOnAdd —— 此时一旦在代码中预置了 Id 值，EF 会误认为该值来自数据库，
        // 从而把“新增文档”判定为已存在（Unchanged/Modified），生成 UPDATE 命中 0 行抛
        // DbUpdateConcurrencyException。显式 ValueGeneratedNever 让 EF 正确认识这是一个新实体。
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.KnowledgeBaseId).IsRequired();
        builder.Property(d => d.DocumentId).IsRequired();
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(500);
        builder.Property(d => d.ContentType).HasMaxLength(200);
        builder.Property(d => d.ChunkCount).IsRequired();
        builder.Property(d => d.CreatedAt).IsRequired();
    }
}
