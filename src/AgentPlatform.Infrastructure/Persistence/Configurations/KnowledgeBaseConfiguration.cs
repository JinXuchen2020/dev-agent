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

        builder.OwnsMany(k => k.Documents, db =>
        {
            db.WithOwner().HasForeignKey("KnowledgeBaseId");
            db.Property<Guid>("Id").ValueGeneratedOnAdd();
            db.HasKey("Id");
            db.Property(d => d.DocumentId).IsRequired();
            db.Property(d => d.FileName).IsRequired().HasMaxLength(500);
            db.Property(d => d.ContentType).HasMaxLength(200);
            db.Property(d => d.ChunkCount).IsRequired();
        });

        builder.Navigation(k => k.Documents)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
