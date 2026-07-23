namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 提供向量存储中文档的入库、检索与删除操作。
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// 将文档入库到指定的向量集合中。
    /// </summary>
    /// <param name="collectionName">目标向量集合名称。</param>
    /// <param name="documentId">文档唯一标识。</param>
    /// <param name="content">需要向量化并存储的文本内容。</param>
    /// <param name="tenantId">所属租户标识，用于多租户隔离。</param>
    /// <param name="metadata">可选的元数据键值对。</param>
    /// <param name="ct">用于观察异步操作完成情况的取消令牌。</param>
    /// <returns>表示异步入库操作的任务。</returns>
    Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Guid tenantId,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// 在指定向量集合中执行语义相似度检索。
    /// </summary>
    /// <param name="collectionName">目标向量集合名称。</param>
    /// <param name="query">用于检索的自然语言查询。</param>
    /// <param name="tenantId">所属租户标识，用于多租户隔离。</param>
    /// <param name="topK">返回结果的最大数量，默认 5。</param>
    /// <param name="minScore">相关性阈值（余弦相似度）。低于该值的结果将被过滤；为 null 时不过滤。</param>
    /// <param name="ct">用于观察异步操作完成情况的取消令牌。</param>
    /// <returns>按相关性排序的只读检索结果列表。</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, Guid tenantId,
        int topK = 5, double? minScore = null,
        CancellationToken ct = default);

    /// <summary>
    /// 从指定向量集合中删除文档。
    /// </summary>
    /// <param name="collectionName">目标向量集合名称。</param>
    /// <param name="documentId">要删除的文档唯一标识。</param>
    /// <param name="tenantId">所属租户标识，用于多租户隔离。</param>
    /// <param name="ct">用于观察异步操作完成情况的取消令牌。</param>
    /// <returns>表示异步删除操作的任务。</returns>
    Task DeleteDocumentAsync(string collectionName, string documentId,
        Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// 根据当前部署配置（数据库类型、PostgreSQL 连接串、OpenAI Key）决定并返回合适的 <see cref="IVectorStore"/> 实现。
/// </summary>
public interface IVectorStoreFactory
{
    /// <summary>
    /// 创建当前部署环境下应使用的向量存储实例。
    /// </summary>
    /// <returns>一个 <see cref="IVectorStore"/> 实现（PostgreSQL pgvector 或进程内回退）。</returns>
    IVectorStore Create();
}

/// <summary>
/// 表示向量相似度检索返回的单个结果。
/// </summary>
/// <param name="DocumentId">匹配文档的唯一标识。</param>
/// <param name="Content">匹配文档的文本内容。</param>
/// <param name="Score">匹配的相关性得分，取值越高表示越相似（余弦相似度）。</param>
/// <param name="Metadata">匹配文档关联的元数据键值对（可选）。</param>
public record VectorSearchResult(
    string DocumentId,
    string Content,
    double Score,
    Dictionary<string, string>? Metadata = null);
