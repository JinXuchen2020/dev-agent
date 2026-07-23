namespace AgentPlatform.Domain.Aggregates.KnowledgeBases;

/// <summary>
/// 知识库中的单个文档（仅持久化元数据；切分后的文本块存储于向量存储）。
/// </summary>
public sealed class KnowledgeDocument
{
    /// <summary>文档实体标识。</summary>
    public Guid Id { get; private set; }

    /// <summary>所属知识库标识。</summary>
    public Guid KnowledgeBaseId { get; private set; }

    /// <summary>与向量存储中的 documentId 对应（一个上传文件对应一个 documentId，可含多个分块）。</summary>
    public Guid DocumentId { get; private set; }

    /// <summary>原始文件名。</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>文件内容类型（MIME）。</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>切分后的分块数量。</summary>
    public int ChunkCount { get; private set; }

    /// <summary>上传时间（UTC）。</summary>
    public DateTime CreatedAt { get; private set; }

    private KnowledgeDocument() { }

    /// <summary>
    /// 创建文档元数据记录。
    /// </summary>
    /// <param name="knowledgeBaseId">所属知识库标识。</param>
    /// <param name="documentId">向量存储中的文档标识。</param>
    /// <param name="fileName">原始文件名。</param>
    /// <param name="contentType">文件内容类型。</param>
    /// <param name="chunkCount">分块数量。</param>
    public static KnowledgeDocument Create(
        Guid knowledgeBaseId, Guid documentId, string fileName, string contentType, int chunkCount)
    {
        return new KnowledgeDocument
        {
            Id = Guid.NewGuid(),
            KnowledgeBaseId = knowledgeBaseId,
            DocumentId = documentId,
            FileName = fileName,
            ContentType = contentType,
            ChunkCount = chunkCount,
            CreatedAt = DateTime.UtcNow
        };
    }
}
