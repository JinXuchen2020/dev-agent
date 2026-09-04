using AgentPlatform.Domain.Aggregates.Conversations;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="Conversation"/> aggregate roots.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Retrieves a conversation by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The conversation if found; otherwise <c>null</c>.</returns>
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a conversation by its unique identifier, eagerly loading all messages.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The conversation with messages if found; otherwise <c>null</c>.</returns>
    Task<Conversation?> GetByIdWithMessagesAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all conversations belonging to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of conversations for the tenant.</returns>
    Task<IReadOnlyList<Conversation>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the agent-owned conversation for a (workflow, agent) pair (F36 per-agent
    /// conversation isolation). Returns the existing conversation for reuse, or null when the
    /// agent step should create one. Messages are not eagerly loaded.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (explicit for clarity; the EF filter also enforces it).</param>
    /// <param name="workflowId">The workflow the conversation is bound to.</param>
    /// <param name="agentId">The owning agent identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The existing agent conversation, or null.</returns>
    Task<Conversation?> GetByAgentAsync(Guid tenantId, Guid workflowId, Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all conversations for a tenant within an optional date range (filtered by
    /// <c>CreatedAt</c>). Used by the analytics summary query for in-memory day-bucket aggregation.
    /// Messages are not eagerly loaded since only <c>TotalTokenUsage</c> and <c>CreatedAt</c> are needed.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter by.</param>
    /// <param name="from">Optional inclusive start date (UTC).</param>
    /// <param name="to">Optional inclusive end date (UTC).</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A read-only list of conversations for the tenant.</returns>
    Task<IReadOnlyList<Conversation>> GetByTenantAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a new conversation to the repository.
    /// </summary>
    /// <param name="conversation">The conversation aggregate to add.</param>
    void Add(Conversation conversation);

    /// <summary>
    /// Updates an existing conversation in the repository.
    /// </summary>
    /// <param name="conversation">The conversation aggregate with modified state.</param>
    void Update(Conversation conversation);

    /// <summary>
    /// Removes a conversation from the repository.
    /// </summary>
    /// <param name="conversation">The conversation aggregate to remove.</param>
    void Remove(Conversation conversation);

    /// <summary>
    /// Detaches a conversation from the persistence change tracker (F36 best-effort
    /// agent-conversation persistence: a failed save must not leave the entity queued for
    /// a retry by an unrelated later save in the same scope).
    /// </summary>
    /// <param name="conversation">The conversation aggregate to detach.</param>
    void Detach(Conversation conversation);
}
