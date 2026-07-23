using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.KnowledgeBases;

/// <summary>
/// 覆盖 R1（KnowledgeBase 聚合）：创建、重命名、描述更新、文档增删、集合名 slug 化、
/// 以及 IAggregateRoot / ITenantScoped 契约。
/// </summary>
public sealed class KnowledgeBaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsIdentityAndTimestamp()
    {
        var kb = KnowledgeBase.Create(_tenantId, "Product Docs", "desc",
            "product-docs-abc12345", "text-embedding-3-small");

        Assert.NotEqual(Guid.Empty, kb.Id);
        Assert.Equal(_tenantId, kb.TenantId);
        Assert.Equal("Product Docs", kb.Name);
        Assert.Equal("desc", kb.Description);
        Assert.Equal("product-docs-abc12345", kb.CollectionName);
        Assert.Equal("text-embedding-3-small", kb.EmbeddingModel);
        Assert.NotEqual(default, kb.CreatedAt);
    }

    [Fact]
    public void Create_RejectsEmptyTenantOrName()
    {
        Assert.Throws<ArgumentException>(() =>
            KnowledgeBase.Create(Guid.Empty, "name", "", "coll", "model"));
        Assert.Throws<ArgumentException>(() =>
            KnowledgeBase.Create(_tenantId, "  ", "", "coll", "model"));
        Assert.Throws<ArgumentException>(() =>
            KnowledgeBase.Create(_tenantId, "name", "", "", "model"));
    }

    [Fact]
    public void BuildCollectionName_SlugsAndAppendsUniqueSuffix()
    {
        var name = KnowledgeBase.BuildCollectionName("My Cool KB!");

        Assert.StartsWith("my-cool-kb-", name);
        Assert.Equal(8, name.Substring("my-cool-kb-".Length).Length);
    }

    [Fact]
    public void AddAndRemoveDocument_UpdatesCollection()
    {
        var kb = KnowledgeBase.Create(_tenantId, "KB", "", "kb-x", "model");
        var docId = Guid.NewGuid();

        var doc = kb.AddDocument(docId, "a.txt", "text/plain", 3);
        Assert.Single(kb.Documents);
        Assert.Equal(docId, doc.DocumentId);
        Assert.Equal("a.txt", doc.FileName);
        Assert.Equal(3, doc.ChunkCount);

        kb.RemoveDocument(docId);
        Assert.Empty(kb.Documents);
    }

    [Fact]
    public void RemoveDocument_UnknownId_IsNoOp()
    {
        var kb = KnowledgeBase.Create(_tenantId, "KB", "", "kb-x", "model");
        kb.AddDocument(Guid.NewGuid(), "a.txt", "text/plain", 1);

        kb.RemoveDocument(Guid.NewGuid());

        Assert.Single(kb.Documents);
    }

    [Fact]
    public void Rename_And_UpdateDescription_ApplyChanges()
    {
        var kb = KnowledgeBase.Create(_tenantId, "KB", "old", "kb-x", "model");

        kb.Rename("New Name");
        kb.UpdateDescription("new desc");

        Assert.Equal("New Name", kb.Name);
        Assert.Equal("new desc", kb.Description);
    }

    [Fact]
    public void AggregateRoot_Contract_ExposesAndClearsDomainEvents()
    {
        var kb = KnowledgeBase.Create(_tenantId, "KB", "", "kb-x", "model");

        // 初始无领域事件；聚合实现 IAggregateRoot 契约（不抛）。
        Assert.Empty(kb.DomainEvents);
        kb.ClearDomainEvents();
        Assert.Empty(kb.DomainEvents);
    }
}
