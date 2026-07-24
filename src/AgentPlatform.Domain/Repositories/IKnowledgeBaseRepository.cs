using AgentPlatform.Domain.Aggregates.KnowledgeBases;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 提供 <see cref="KnowledgeBase"/> 聚合根的持久化与查询操作。
/// </summary>
public interface IKnowledgeBaseRepository
{
    /// <summary>按唯一标识获取知识库（含文档）。</summary>
    Task<KnowledgeBase?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>获取指定租户下的全部知识库（含文档）。</summary>
    Task<IReadOnlyList<KnowledgeBase>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>新增知识库。</summary>
    void Add(KnowledgeBase knowledgeBase);

    /// <summary>更新知识库。</summary>
    void Update(KnowledgeBase knowledgeBase);

    /// <summary>移除知识库。</summary>
    void Remove(KnowledgeBase knowledgeBase);
}
