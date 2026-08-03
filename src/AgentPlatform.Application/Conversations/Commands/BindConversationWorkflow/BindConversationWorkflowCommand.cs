using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.BindConversationWorkflow;

/// <summary>将某会话绑定到一个工作流（Chat 触发器）。幂等：已绑定则直接返回成功。</summary>
/// <param name="ConversationId">会话标识。</param>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">租户（会话与工作流均须归属该租户）。</param>
public record BindConversationWorkflowCommand(Guid ConversationId, Guid WorkflowId, Guid TenantId)
    : IRequest<bool>;
