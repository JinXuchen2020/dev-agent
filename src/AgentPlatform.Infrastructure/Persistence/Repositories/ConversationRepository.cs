using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IConversationRepository"/> for persisting and querying conversation aggregates.
/// </summary>
internal sealed class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationRepository"/> class.
    /// </summary>
    /// <param name="context">The application database context used for data access.</param>
    public ConversationRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves a conversation by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="Conversation"/>, or <c>null</c> if not found.</returns>
    public async Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Conversations.FindAsync([id], ct);
    }

    /// <summary>
    /// Retrieves a conversation by its identifier, eagerly loading its associated messages.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="Conversation"/> including messages, or <c>null</c> if not found.</returns>
    public async Task<Conversation?> GetByIdWithMessagesAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    /// <summary>
    /// Retrieves all conversations belonging to the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter conversations by.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of conversations for the tenant.</returns>
    public async Task<IReadOnlyList<Conversation>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Conversations
            .Where(c => c.TenantId == tenantId)
            .Include(c => c.Messages)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> GetByTenantAsync(
        Guid tenantId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = _context.Conversations
            .Where(c => c.TenantId == tenantId)
            .AsQueryable();

        if (from.HasValue)
            query = query.Where(c => c.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(c => c.CreatedAt <= to.Value);

        return await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Adds a new conversation aggregate to the change tracker.
    /// </summary>
    /// <param name="conversation">The conversation aggregate to add.</param>
    public void Add(Conversation conversation)
    {
        _context.Conversations.Add(conversation);
    }

    /// <summary>
    /// Marks the specified conversation aggregate as modified so it is updated on the next save.
    /// </summary>
    /// <param name="conversation">The conversation aggregate to update.</param>
    public void Update(Conversation conversation)
    {
        _context.Conversations.Update(conversation);
    }

    /// <summary>
    /// Marks the specified conversation aggregate for deletion on the next save.
    /// </summary>
    /// <param name="conversation">The conversation aggregate to remove.</param>
    public void Remove(Conversation conversation)
    {
        _context.Conversations.Remove(conversation);
    }
}
