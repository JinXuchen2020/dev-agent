using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.KnowledgeBases;

/// <summary>
/// 知识库聚合根：归属于某个租户，持有若干文档（<see cref="KnowledgeDocument"/>）。
/// <see cref="CollectionName"/> 为 slug 化的唯一标识，用于路由向量存储集合。
/// </summary>
public sealed class KnowledgeBase : ITenantScoped, IAggregateRoot
{
    /// <summary>知识库唯一标识。</summary>
    public Guid Id { get; private set; }

    /// <summary>所属租户标识（多租户隔离键）。</summary>
    public Guid TenantId { get; private set; }

    /// <summary>知识库显示名称。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>知识库描述（可选）。</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>向量存储集合名称（slug 化、租户内唯一）。</summary>
    public string CollectionName { get; private set; } = string.Empty;

    /// <summary>用于该知识库 embedding 的模型名称。</summary>
    public string EmbeddingModel { get; private set; } = string.Empty;

    /// <summary>创建时间（UTC）。</summary>
    public DateTime CreatedAt { get; private set; }

    private readonly List<KnowledgeDocument> _documents = new();
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>该知识库下的文档列表（只读）。</summary>
    public IReadOnlyList<KnowledgeDocument> Documents => _documents.AsReadOnly();

    /// <summary>该聚合已触发但尚未分发的领域事件集合。</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>在领域事件分发后清空待处理事件。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    private KnowledgeBase() { }

    /// <summary>
    /// 创建新知识库聚合。
    /// </summary>
    /// <param name="tenantId">所属租户标识。</param>
    /// <param name="name">知识库名称。</param>
    /// <param name="description">知识库描述（可为空）。</param>
    /// <param name="collectionName">向量存储集合名（slug 化）。</param>
    /// <param name="embeddingModel">embedding 模型名称。</param>
    public static KnowledgeBase Create(
        Guid tenantId, string name, string description, string collectionName, string embeddingModel)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("租户标识不能为空", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("知识库名称不能为空", nameof(name));
        if (string.IsNullOrWhiteSpace(collectionName))
            throw new ArgumentException("集合名称不能为空", nameof(collectionName));

        return new KnowledgeBase
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            CollectionName = collectionName,
            EmbeddingModel = embeddingModel,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>重命名知识库。</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("知识库名称不能为空", nameof(name));
        Name = name;
    }

    /// <summary>更新知识库描述。</summary>
    public void UpdateDescription(string description) => Description = description;

    /// <summary>向知识库添加一个已入库的文档元数据记录。</summary>
    public KnowledgeDocument AddDocument(Guid documentId, string fileName, string contentType, int chunkCount)
    {
        var doc = KnowledgeDocument.Create(Id, documentId, fileName, contentType, chunkCount);
        _documents.Add(doc);
        return doc;
    }

    /// <summary>按文档标识从知识库移除文档元数据记录。</summary>
    public void RemoveDocument(Guid documentId)
    {
        var doc = _documents.FirstOrDefault(d => d.DocumentId == documentId);
        if (doc is not null)
            _documents.Remove(doc);
    }

    /// <summary>将名称转换为 slug 形式的集合名，并追加短随机后缀以保证租户内唯一。</summary>
    public static string BuildCollectionName(string name)
    {
        var slug = string.Concat((name ?? "kb")
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == ' '))
            .Trim()
            .Replace(' ', '-');
        slug = string.IsNullOrWhiteSpace(slug) ? "kb" : slug;
        return $"{slug}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}
