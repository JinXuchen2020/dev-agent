using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.UnbindConversationWorkflow;

/// <summary>解除某会话对某一工作流的绑定（Chat 触发器）。幂等：未绑定也返回成功。</summary>
/// <param name="ConversationId">会话标识。</param>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">租户。</param>
public record UnbindConversationWorkflowCommand(Guid ConversationId, Guid WorkflowId, Guid TenantId)
    : IRequest<bool>;
