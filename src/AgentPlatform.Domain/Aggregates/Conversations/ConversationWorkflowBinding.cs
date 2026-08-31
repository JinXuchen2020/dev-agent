using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Conversations;

/// <summary>
/// Chat 触发器绑定：将一个会话绑定到一个工作流，使该会话可主动触发该工作流（v1 显式按钮/指令）。
/// 一个会话可绑定多个工作流（多对多），故独立于 <c>Conversation.WorkflowId</c> 遗留单列存在。
/// </summary>
public sealed class ConversationWorkflowBinding : ITenantScoped, IWorkspaceScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the binding.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the identifier of the conversation.</summary>
    public Guid ConversationId { get; private init; }

    /// <summary>Gets the identifier of the bound workflow.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Gets the identifier of the tenant that owns this binding.</summary>
    public Guid TenantId { get; private init; }
    public Guid WorkspaceId { get; private init; }

    /// <summary>Gets the UTC creation time.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <inheritdoc />
    public void ClearDomainEvents() { }

    private ConversationWorkflowBinding() { }

    /// <summary>Initializes a new conversation-to-workflow binding.</summary>
    public ConversationWorkflowBinding(Guid id, Guid conversationId, Guid workflowId, Guid tenantId)
    {
        Id = id;
        ConversationId = conversationId;
        WorkflowId = workflowId;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
    }
}
