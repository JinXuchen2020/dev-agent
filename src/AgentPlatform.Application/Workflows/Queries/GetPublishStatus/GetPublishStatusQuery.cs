using AgentPlatform.Application.PublishedWorkflows;

namespace AgentPlatform.Application.Workflows.Queries.GetPublishStatus;

/// <summary>
/// 查询某工作流的发布状态（F22）。未发布返回 null（控制器映射为 204）。
/// </summary>
public record GetPublishStatusQuery(
    Guid WorkflowId,
    Guid TenantId
) : MediatR.IRequest<PublishStatusResponse?>;
