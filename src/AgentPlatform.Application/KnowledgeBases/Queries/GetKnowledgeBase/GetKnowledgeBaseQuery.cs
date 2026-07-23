using AgentPlatform.Application.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.KnowledgeBases.Queries.GetKnowledgeBase;

/// <summary>
/// 获取单个知识库详情（含文档列表）。
/// 若知识库不存在或不属于当前租户，返回 null（控制器映射为 404）。
/// </summary>
/// <param name="TenantId">当前租户标识（由控制器解析）。</param>
/// <param name="Id">知识库标识。</param>
public record GetKnowledgeBaseQuery(Guid TenantId, Guid Id)
    : IRequest<KnowledgeBaseResponse?>;

/// <summary>处理 <see cref="GetKnowledgeBaseQuery"/>，返回知识库详情或 null。</summary>
internal sealed class GetKnowledgeBaseQueryHandler
    (IKnowledgeBaseRepository repository)
    : IRequestHandler<GetKnowledgeBaseQuery, KnowledgeBaseResponse?>
{
    public async Task<KnowledgeBaseResponse?> Handle(GetKnowledgeBaseQuery request, CancellationToken ct)
    {
        var kb = await repository.GetByIdAsync(request.Id, ct);
        if (kb is null || kb.TenantId != request.TenantId)
            return null;

        return KnowledgeBaseResponses.ToResponse(kb);
    }
}
