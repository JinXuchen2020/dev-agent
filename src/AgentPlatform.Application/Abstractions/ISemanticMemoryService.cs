namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 语义记忆服务（F33）：跨运行经验沉淀与语义召回。
/// 底层复用 IVectorStore（租户隔离 + Pg/InMemory 双实现）；本抽象屏蔽集合名与内容模板细节。
/// </summary>
public interface ISemanticMemoryService
{
    /// <summary>
    /// 将一次工作流运行的经验（成功产出或失败教训）写入语义记忆。
    /// 幂等性由调用方保证（每次运行的 digest 文档 id 唯一）。
    /// </summary>
    /// <param name="tenantId">租户标识（隔离维度）。</param>
    /// <param name="workflowId">工作流标识。</param>
    /// <param name="workflowName">工作流名称。</param>
    /// <param name="outcome">结局：completed / rolled_back 等。</param>
    /// <param name="digest">步骤产出的聚合摘要文本。</param>
    /// <param name="ct">取消令牌。</param>
    Task RememberRunAsync(Guid tenantId, Guid workflowId, string workflowName,
        string outcome, string digest, CancellationToken ct = default);

    /// <summary>
    /// 按查询文本做语义召回，返回最相关的历史经验。
    /// </summary>
    /// <param name="tenantId">租户标识（隔离维度）。</param>
    /// <param name="query">召回查询文本。</param>
    /// <param name="topK">最大返回条数。</param>
    /// <param name="minScore">相关性阈值下限。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<VectorSearchResult>> RecallAsync(
        Guid tenantId, string query, int topK, double minScore, CancellationToken ct = default);
}