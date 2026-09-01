using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.GetConversations;

/// <summary>
/// Represents a query to retrieve conversations belonging to the current tenant,
/// optionally filtered by lifecycle <paramref name="Status"/> and a free-text
/// <paramref name="Q"/> match across the conversation id, workflow id, knowledge
/// base id, collection name, and message contents, and optionally by the owning
/// <paramref name="AgentId"/> (F36 per-agent conversation isolation).
/// </summary>
public record GetConversationsQuery(ConversationStatus? Status = null, string? Q = null, Guid? AgentId = null)
    : IRequest<IEnumerable<Conversation>>;
