using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.ListConversationWorkflowBindings;

/// <summary>列出某会话绑定的全部工作流（受租户过滤），含工作流名称。</summary>
/// <param name="ConversationId">会话标识。</param>
/// <param name="TenantId">租户。</param>
public record ListConversationWorkflowBindingsQuery(Guid ConversationId, Guid TenantId)
    : IRequest<IReadOnlyList<WorkflowBindingDto>>;

/// <summary>会话绑定的工作流视图。</summary>
public sealed record WorkflowBindingDto(Guid WorkflowId, string WorkflowName);
