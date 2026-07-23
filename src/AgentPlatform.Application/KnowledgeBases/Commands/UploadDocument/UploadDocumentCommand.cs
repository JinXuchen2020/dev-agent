using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.KnowledgeBases.Commands.UploadDocument;

/// <summary>
/// 上传文档到知识库：切分后逐块入库向量存储，并记录文档元数据。
/// 若知识库不存在或不属于当前租户，返回 null（控制器映射为 404）。
/// </summary>
/// <param name="TenantId">当前租户标识（由控制器解析）。</param>
/// <param name="KnowledgeBaseId">目标知识库标识。</param>
/// <param name="FileName">原始文件名。</param>
/// <param name="ContentType">文件内容类型（MIME）。</param>
/// <param name="Content">文档文本内容。</param>
public record UploadDocumentCommand(
    Guid TenantId,
    Guid KnowledgeBaseId,
    string FileName,
    string ContentType,
    string Content)
    : ICommand<KnowledgeDocumentResponse?>;

/// <summary>处理 <see cref="UploadDocumentCommand"/>：切分、入库向量存储、记录元数据。</summary>
internal sealed class UploadDocumentCommandHandler
    (IKnowledgeBaseRepository repository,
     IVectorStore vectorStore,
     IDocumentChunker chunker)
    : IRequestHandler<UploadDocumentCommand, KnowledgeDocumentResponse?>
{
    public async Task<KnowledgeDocumentResponse?> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        var kb = await repository.GetByIdAsync(request.KnowledgeBaseId, ct);
        if (kb is null || kb.TenantId != request.TenantId)
            return null;

        var chunks = chunker.Chunk(request.Content);
        if (chunks.Count == 0)
            return null;

        var documentId = Guid.NewGuid();
        foreach (var chunk in chunks)
        {
            await vectorStore.IngestDocumentAsync(
                kb.CollectionName,
                documentId.ToString(),
                chunk.Content,
                request.TenantId,
                new Dictionary<string, string> { ["chunkIndex"] = chunk.Index.ToString() },
                ct);
        }

        var doc = kb.AddDocument(documentId, request.FileName, request.ContentType, chunks.Count);
        repository.Update(kb); // 受跟踪聚合，由 UnitOfWorkBehavior 提交

        return KnowledgeBaseResponses.ToResponse(doc);
    }
}
