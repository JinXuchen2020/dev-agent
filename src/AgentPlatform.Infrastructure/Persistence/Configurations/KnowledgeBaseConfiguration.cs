using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// 配置 <see cref="KnowledgeBase"/> 聚合的 EF Core 映射，包含拥有的文档集合（KnowledgeDocument）。
/// 聚合根实现 <see cref="AgentPlatform.Domain.Abstractions.ITenantScoped"/>，
/// 自动受到 AppDbContext 全局租户查询过滤器隔离。
/// </summary>
internal sealed class KnowledgeBaseConfiguration : IEntityTypeConfiguration<KnowledgeBase>
{
    /// <summary>配置知识库实体类型映射。</summary>
    public void Configure(EntityTypeBuilder<KnowledgeBase> builder)
    {
        builder.ToTable("KnowledgeBases");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.TenantId).IsRequired();
        builder.Property(k => k.Name).IsRequired().HasMaxLength(200);
        builder.Property(k => k.Description).HasMaxLength(4000);
        builder.Property(k => k.CollectionName).IsRequired().HasMaxLength(200);
        builder.Property(k => k.EmbeddingModel).HasMaxLength(200);

        // KnowledgeDocument 拥有独立标识（自有 Id，且被向量存储按 DocumentId 外部引用），
        // 语义上是“被引用的实体”而非“值对象”，因此用常规一对多（HasMany）而非 OwnsMany。
        // 关键修复：OwnsMany + 预置独立 Id 会导致 EF 把“新增子实体”误判为 Modified，
        // 生成 UPDATE 命中 0 行抛 DbUpdateConcurrencyException；改为 HasMany 后，
        // 新增文档会被正确标记为 Added → INSERT。
        builder.HasMany(k => k.Documents)
            .WithOne()
            .HasForeignKey("KnowledgeBaseId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(k => k.Documents)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
