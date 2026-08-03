using AgentPlatform.Domain.Aggregates.Conversations;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Chat 触发器绑定持久化与查询。所有查询受 AppDbContext 全局 ITenantScoped 过滤器约束。
/// </summary>
public interface IConversationWorkflowBindingRepository
{
    /// <summary>列出某会话绑定的全部工作流（受租户过滤）。</summary>
    Task<IReadOnlyList<ConversationWorkflowBinding>> GetByConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>查找某会话对某一工作流的绑定（受租户过滤）。</summary>
    Task<ConversationWorkflowBinding?> GetAsync(Guid conversationId, Guid workflowId, CancellationToken ct = default);

    /// <summary>列出绑定了某工作流的全部会话（受租户过滤，用于级联清理）。</summary>
    Task<IReadOnlyList<ConversationWorkflowBinding>> GetByWorkflowAsync(Guid workflowId, CancellationToken ct = default);

    void Add(ConversationWorkflowBinding binding);

    void Remove(ConversationWorkflowBinding binding);
}
