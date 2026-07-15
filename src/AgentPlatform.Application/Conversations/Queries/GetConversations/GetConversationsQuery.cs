using MediatR;
using AgentPlatform.Domain.Aggregates.Conversations;

namespace AgentPlatform.Application.Conversations.Queries.GetConversations;

/// <summary>
/// Represents a query to retrieve all conversations belonging to the current tenant.
/// </summary>
public record GetConversationsQuery : IRequest<IEnumerable<Conversation>>;
