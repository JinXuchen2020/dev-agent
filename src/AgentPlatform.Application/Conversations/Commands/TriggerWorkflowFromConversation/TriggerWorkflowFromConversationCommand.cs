using AgentPlatform.Application.WorkflowTriggers;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.TriggerWorkflowFromConversation;

/// <summary>
/// Chat 触发：校验会话归属与绑定关系后，委托 <see cref="TriggerWorkflowCommand"/>（TriggerType.Chat）
/// 运行绑定工作流。无绑定 / 越界 / 工作流不存在 → 返回 null（控制器映射 404）。
/// </summary>
/// <param name="ConversationId">会话标识。</param>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">租户（会话与工作流均须归属该租户，且存在绑定）。</param>
public record TriggerWorkflowFromConversationCommand(
    Guid ConversationId, Guid WorkflowId, Guid TenantId)
    : IRequest<TriggerRunResult?>;
