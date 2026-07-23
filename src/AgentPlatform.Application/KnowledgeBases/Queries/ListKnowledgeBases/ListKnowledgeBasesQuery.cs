using AgentPlatform.Application.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.KnowledgeBases.Queries.ListKnowledgeBases;

/// <summary>
/// 列出当前租户下的全部知识库（含文档列表）。
/// </summary>
/// <param name="TenantId">当前租户标识（由控制器解析）。</param>
public record ListKnowledgeBasesQuery(Guid TenantId)
    : IRequest<IReadOnlyList<KnowledgeBaseResponse>>;

/// <summary>处理 <see cref="ListKnowledgeBasesQuery"/>，按租户返回知识库列表。</summary>
internal sealed class ListKnowledgeBasesQueryHandler
    (IKnowledgeBaseRepository repository)
    : IRequestHandler<ListKnowledgeBasesQuery, IReadOnlyList<KnowledgeBaseResponse>>
{
    public async Task<IReadOnlyList<KnowledgeBaseResponse>> Handle(
        ListKnowledgeBasesQuery request, CancellationToken ct)
    {
        var list = await repository.GetByTenantAsync(request.TenantId, ct);
        return list.Select(KnowledgeBaseResponses.ToResponse).ToList();
    }
}
