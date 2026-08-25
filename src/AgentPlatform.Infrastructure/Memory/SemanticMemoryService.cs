using System.Security.Cryptography;
using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Infrastructure.Memory;

/// <summary>
/// 语义记忆服务实现（F33）：复用 IVectorStore 的向量化与租户隔离能力，
/// 在独立集合 "semantic-memory" 中沉淀跨运行 episodic 经验并支持语义召回。
/// 文档 id 由内容哈希派生——同工作流同结局同摘要不重复堆积。
/// </summary>
internal sealed class SemanticMemoryService : ISemanticMemoryService
{
    private readonly IVectorStore _vectorStore;

    public SemanticMemoryService(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore;
    }

    /// <inheritdoc />
    public Task RememberRunAsync(Guid tenantId, Guid workflowId, string workflowName,
        string outcome, string digest, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        var content = $"[episodic:{outcome}] workflow={workflowName}({workflowId})\n{digest}";
        var documentId = MemoryDocId(workflowId, outcome, digest);
        var metadata = new Dictionary<string, string>
        {
            ["kind"] = "run",
            ["workflowId"] = workflowId.ToString(),
            ["outcome"] = outcome,
            ["engine"] = "f33"
        };

        return _vectorStore.IngestDocumentAsync(
            RoutingConstants.SemanticMemoryCollection, documentId,
            content, tenantId, metadata, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorSearchResult>> RecallAsync(
        Guid tenantId, string query, int topK, double minScore, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _vectorStore.SearchAsync(
            RoutingConstants.SemanticMemoryCollection, query, tenantId,
            topK: topK, minScore: minScore, ct);
    }

    /// <summary>内容寻址 id：同 wf+结局+摘要（截断参与哈希）稳定去重。</summary>
    private static string MemoryDocId(Guid workflowId, string outcome, string digest)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{workflowId}:{outcome}:{digest}"));
        return $"mem-{workflowId:N}-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }
}