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
}
