using AgentPlatform.Domain.Aggregates.KnowledgeBases;

namespace AgentPlatform.Application.KnowledgeBases;

/// <summary>
/// 知识库相关响应模型与聚合到响应的映射。
/// </summary>
public sealed record KnowledgeBaseResponse(
    Guid Id,
    string Name,
    string Description,
    string CollectionName,
    string EmbeddingModel,
    DateTime CreatedAt,
    IReadOnlyList<KnowledgeDocumentResponse> Documents);

/// <summary>
/// 知识库中文档的响应模型。
/// </summary>
public sealed record KnowledgeDocumentResponse(
    Guid Id,
    Guid DocumentId,
    string FileName,
    string ContentType,
    int ChunkCount,
    DateTime CreatedAt);

/// <summary>
/// 提供 <see cref="KnowledgeBase"/> / <see cref="KnowledgeDocument"/> 到响应模型的映射。
/// </summary>
public static class KnowledgeBaseResponses
{
    /// <summary>将知识库聚合映射为响应模型。</summary>
    public static KnowledgeBaseResponse ToResponse(KnowledgeBase kb) => new(
        kb.Id,
        kb.Name,
        kb.Description,
        kb.CollectionName,
        kb.EmbeddingModel,
        kb.CreatedAt,
        kb.Documents.Select(ToResponse).ToList());

    /// <summary>将文档元数据映射为响应模型。</summary>
    public static KnowledgeDocumentResponse ToResponse(KnowledgeDocument doc) => new(
        doc.Id,
        doc.DocumentId,
        doc.FileName,
        doc.ContentType,
        doc.ChunkCount,
        doc.CreatedAt);
}
