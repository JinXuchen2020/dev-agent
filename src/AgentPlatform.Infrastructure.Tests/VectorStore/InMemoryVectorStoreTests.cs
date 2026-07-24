using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.VectorStore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.VectorStore;

/// <summary>
/// 覆盖 RAG 地基层 R1-R4 的核心检索验收项：
/// 入库后 SearchAsync 能返回 &gt;0 条、跨租户回归、minScore 低分噪声过滤、多分块与删除。
/// 使用进程内确定性伪向量实现，不依赖外部 embedding 服务或 PostgreSQL。
/// </summary>
public sealed class InMemoryVectorStoreTests
{
    private readonly ILogger<InMemoryVectorStore> _logger = Substitute.For<ILogger<InMemoryVectorStore>>();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private const string Collection = "kb-test";

    private InMemoryVectorStore CreateStore() => new(_logger);

    [Fact]
    public async Task Ingest_ThenSearch_ReturnsMatchingDocument()
    {
        var store = CreateStore();
        await store.IngestDocumentAsync(Collection, "doc-1",
            "kubernetes pods deployment scaling containers orchestration", _tenantA);

        var results = await store.SearchAsync(Collection, "how to scale kubernetes pods", _tenantA);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.DocumentId == "doc-1");
        Assert.All(results, r => Assert.True(r.Score > 0, "相关文档得分应为正"));
    }

    [Fact]
    public async Task Ingest_EmptyStore_ReturnsZeroResults_NotThrows()
    {
        var store = CreateStore();

        var results = await store.SearchAsync(Collection, "anything", _tenantA);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CrossTenant_Isolation_TenantBSeesNothingIngestedByTenantA()
    {
        var store = CreateStore();
        await store.IngestDocumentAsync(Collection, "doc-1",
            "confidential tenant A knowledge base content", _tenantA);

        var fromB = await store.SearchAsync(Collection, "confidential tenant knowledge", _tenantB);
        var fromA = await store.SearchAsync(Collection, "confidential tenant knowledge", _tenantA);

        Assert.Empty(fromB);
        Assert.NotEmpty(fromA);
    }

    [Fact]
    public async Task MinScore_FiltersLowSimilarityNoise()
    {
        var store = CreateStore();
        await store.IngestDocumentAsync(Collection, "related",
            "alpha beta gamma delta epsilon zeta", _tenantA);
        await store.IngestDocumentAsync(Collection, "noise",
            "zzz qqq www eee rrr ttt completely unrelated", _tenantA);

        var query = "alpha beta gamma delta epsilon zeta";

        var withoutThreshold = await store.SearchAsync(Collection, query, _tenantA, minScore: null);
        var withThreshold = await store.SearchAsync(Collection, query, _tenantA, minScore: 0.99);

        // 无阈值：两份都返回（related 满分，noise 0 分）
        Assert.Equal(2, withoutThreshold.Count);
        // 高阈值：噪声被过滤，仅保留精确匹配
        Assert.Single(withThreshold);
        Assert.Equal("related", withThreshold[0].DocumentId);
        Assert.Equal(1.0, withThreshold[0].Score, precision: 5);
    }

    [Fact]
    public async Task MultipleChunks_SameDocumentId_AreAllRetrievable()
    {
        var store = CreateStore();
        var docId = "doc-multi";
        await store.IngestDocumentAsync(Collection, docId, "first chunk about authentication", _tenantA);
        await store.IngestDocumentAsync(Collection, docId, "second chunk about authorization", _tenantA);

        var results = await store.SearchAsync(Collection, "authentication authorization", _tenantA);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(docId, r.DocumentId));
    }

    [Fact]
    public async Task Delete_RemovesDocumentFromSearchResults()
    {
        var store = CreateStore();
        await store.IngestDocumentAsync(Collection, "doc-1", "deletable content here", _tenantA);

        var before = await store.SearchAsync(Collection, "deletable content", _tenantA);
        Assert.NotEmpty(before);

        await store.DeleteDocumentAsync(Collection, "doc-1", _tenantA);

        var after = await store.SearchAsync(Collection, "deletable content", _tenantA);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Delete_IsTenantScoped_OtherTenantUnaffected()
    {
        var store = CreateStore();
        await store.IngestDocumentAsync(Collection, "doc-1", "shared content between tenants", _tenantA);
        await store.IngestDocumentAsync(Collection, "doc-1", "shared content between tenants", _tenantB);

        await store.DeleteDocumentAsync(Collection, "doc-1", _tenantA);

        var fromB = await store.SearchAsync(Collection, "shared content", _tenantB);
        Assert.NotEmpty(fromB);
        var fromA = await store.SearchAsync(Collection, "shared content", _tenantA);
        Assert.Empty(fromA);
    }
}
