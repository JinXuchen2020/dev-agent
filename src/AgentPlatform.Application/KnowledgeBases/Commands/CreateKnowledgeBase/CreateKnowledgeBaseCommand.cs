using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.KnowledgeBases;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.KnowledgeBases.Commands.CreateKnowledgeBase;

/// <summary>
/// 创建新知识库（租户隔离）。返回建好的知识库响应（含集合名）。
/// </summary>
/// <param name="TenantId">所属租户标识（由控制器解析）。</param>
/// <param name="Name">知识库名称。</param>
/// <param name="Description">知识库描述（可选）。</param>
/// <param name="EmbeddingModel">embedding 模型（可选，默认取配置）。</param>
public record CreateKnowledgeBaseCommand(
    Guid TenantId,
    string Name,
    string? Description = null,
    string? EmbeddingModel = null)
    : ICommand<KnowledgeBaseResponse>;

/// <summary>处理 <see cref="CreateKnowledgeBaseCommand"/>，创建并持久化知识库聚合。</summary>
internal sealed class CreateKnowledgeBaseCommandHandler
    (IKnowledgeBaseRepository repository, IOptions<RagSettings> ragOptions)
    : IRequestHandler<CreateKnowledgeBaseCommand, KnowledgeBaseResponse>
{
    public Task<KnowledgeBaseResponse> Handle(CreateKnowledgeBaseCommand request, CancellationToken ct)
    {
        var embeddingModel = request.EmbeddingModel ?? ragOptions.Value.EmbeddingModel;
        var collectionName = KnowledgeBase.BuildCollectionName(request.Name);
        var kb = KnowledgeBase.Create(
            request.TenantId, request.Name, request.Description ?? string.Empty, collectionName, embeddingModel);

        repository.Add(kb); // 受跟踪聚合，由 UnitOfWorkBehavior 提交

        return Task.FromResult(KnowledgeBaseResponses.ToResponse(kb));
    }
}
