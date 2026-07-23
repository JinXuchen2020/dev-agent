using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.KnowledgeBases.Commands.DeleteKnowledgeBase;

/// <summary>
/// 删除知识库：级联删除其所有文档在向量存储中的分块，再移除聚合。
/// 返回 true 表示已删除；false 表示知识库不存在或不属于当前租户（控制器映射为 404）。
/// </summary>
/// <param name="TenantId">当前租户标识（由控制器解析）。</param>
/// <param name="KnowledgeBaseId">目标知识库标识。</param>
public record DeleteKnowledgeBaseCommand(Guid TenantId, Guid KnowledgeBaseId)
    : ICommand<bool>;

/// <summary>处理 <see cref="DeleteKnowledgeBaseCommand"/>：级联删除向量分块并移除聚合。</summary>
internal sealed class DeleteKnowledgeBaseCommandHandler
    (IKnowledgeBaseRepository repository, IVectorStore vectorStore)
    : IRequestHandler<DeleteKnowledgeBaseCommand, bool>
{
    public async Task<bool> Handle(DeleteKnowledgeBaseCommand request, CancellationToken ct)
    {
        var kb = await repository.GetByIdAsync(request.KnowledgeBaseId, ct);
        if (kb is null || kb.TenantId != request.TenantId)
            return false;

        foreach (var doc in kb.Documents)
        {
            await vectorStore.DeleteDocumentAsync(
                kb.CollectionName, doc.DocumentId.ToString(), request.TenantId, ct);
        }

        repository.Remove(kb); // 受跟踪聚合，由 UnitOfWorkBehavior 提交
        return true;
    }
}
