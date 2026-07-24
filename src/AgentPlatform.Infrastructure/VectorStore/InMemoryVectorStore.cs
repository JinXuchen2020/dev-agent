using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.VectorStore;

/// <summary>
/// 进程内向量存储实现，作为默认（SQLite）部署或缺少 OpenAI Key 时的回退方案。
/// 使用确定性的哈希伪向量（不依赖外部 embedding 服务），仅供本地开发与测试使用，
/// 不保证生产级语义检索质量。支持多租户隔离、相关度阈值过滤以及同一文档的多个分块。
/// </summary>
internal sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ILogger<InMemoryVectorStore> _logger;
    // 进程内回退存储：以 EntryId 为键的并发字典，删除走原子 TryRemove，
    // 避免 ConcurrentBag + 惰性 IsDeleted 标记带来的跨请求数据残留与读写竞态。
    private readonly ConcurrentDictionary<Guid, StoredEntry> _entries = new();

    /// <summary>
    /// 伪向量维度（与 text-embedding-3-small 对齐，仅用于本地计算，不影响外部服务）。
    /// </summary>
    private const int EmbeddingDimension = 1536;

    /// <summary>
    /// 初始化 <see cref="InMemoryVectorStore"/> 的新实例。
    /// </summary>
    /// <param name="logger">用于记录向量存储操作的日志器。</param>
    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task IngestDocumentAsync(string collectionName, string documentId,
        string content, Guid tenantId,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var vector = Embed(content);
        var entryId = Guid.NewGuid();
        _entries.TryAdd(entryId, new StoredEntry(
            entryId, collectionName, documentId, tenantId, content, vector, metadata));
        _logger.LogDebug(
            "Ingested chunk for document {DocId} into collection {Collection} for tenant {Tenant}",
            documentId, collectionName, tenantId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName, string query, Guid tenantId,
        int topK = 5, double? minScore = null,
        CancellationToken ct = default)
    {
        var queryVector = Embed(query);
        var results = new List<VectorSearchResult>();

        foreach (var entry in _entries.Values)
        {
            if (entry.CollectionName != collectionName)
                continue;
            if (entry.TenantId != tenantId)
                continue;

            var score = CosineSimilarity(queryVector, entry.Vector);
            if (minScore.HasValue && score < minScore.Value)
                continue;

            results.Add(new VectorSearchResult(
                entry.DocumentId, entry.Content, score, entry.Metadata));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (results.Count > topK)
            results = results.GetRange(0, topK);

        _logger.LogDebug(
            "In-memory search in collection {Collection} for tenant {Tenant} returned {Count} result(s)",
            collectionName, tenantId, results.Count);

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    /// <inheritdoc/>
    public Task DeleteDocumentAsync(string collectionName, string documentId,
        Guid tenantId, CancellationToken ct = default)
    {
        var removed = 0;
        foreach (var entry in _entries.Values)
        {
            if (entry.CollectionName == collectionName
                && entry.DocumentId == documentId
                && entry.TenantId == tenantId)
            {
                // 原子移除整条分块记录，彻底释放内存（无惰性标记残留）。
                if (_entries.TryRemove(entry.EntryId, out _))
                    removed++;
            }
        }

        _logger.LogDebug(
            "Deleted {Count} chunk(s) for document {DocId} from collection {Collection} for tenant {Tenant}",
            removed, documentId, collectionName, tenantId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 由文本生成确定性的归一化伪向量：对词与字符三元组做哈希分桶并累加后 L2 归一化。
    /// 相同文本始终得到相同向量，因此可用于本地相似度估算。
    /// </summary>
    private static float[] Embed(string text)
    {
        var vec = new float[EmbeddingDimension];
        if (string.IsNullOrWhiteSpace(text))
            return Normalize(vec);

        var lower = text.ToLowerInvariant();
        var tokens = lower.Split([' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var idx = Math.Abs(Hash(token) % EmbeddingDimension);
            vec[idx] += 1f;
        }

        for (var i = 0; i + 2 < lower.Length; i++)
        {
            var trigram = lower.Substring(i, 3);
            var idx = Math.Abs(Hash(trigram) % EmbeddingDimension);
            vec[idx] += 0.5f;
        }

        return Normalize(vec);
    }

    private static float[] Normalize(float[] vec)
    {
        var norm = 0d;
        foreach (var v in vec)
            norm += v * v;
        norm = Math.Sqrt(norm);
        if (norm <= double.Epsilon)
            return vec;
        for (var i = 0; i < vec.Length; i++)
            vec[i] = (float)(vec[i] / norm);
        return vec;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0d;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot; // both vectors are unit-normalized
    }

    private static int Hash(string s)
    {
        // FNV-1a 32-bit
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private sealed record StoredEntry(
        Guid EntryId,
        string CollectionName,
        string DocumentId,
        Guid TenantId,
        string Content,
        float[] Vector,
        Dictionary<string, string>? Metadata);
}
