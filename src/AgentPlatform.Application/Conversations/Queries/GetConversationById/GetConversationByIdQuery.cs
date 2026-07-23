using AgentPlatform.Domain.Aggregates.Conversations;
using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.GetConversationById;

/// <summary>
/// Represents a query to retrieve a single conversation (with its messages) by identifier.
/// </summary>
/// <param name="Id">The unique identifier of the conversation.</param>
public record GetConversationByIdQuery(Guid Id) : IRequest<Conversation?>;
