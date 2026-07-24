using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;

namespace AgentPlatform.Application.Conversations.Commands.SetConversationKnowledgeBase;

/// <summary>
/// Links a conversation to a tenant-owned knowledge base so its messages are grounded in that KB's vector collection.
/// </summary>
/// <param name="ConversationId">The unique identifier of the conversation to link.</param>
/// <param name="KnowledgeBaseId">The unique identifier of the knowledge base to attach.</param>
/// <param name="TenantId">The current tenant identifier (used for ownership validation).</param>
public record SetConversationKnowledgeBaseCommand(
    Guid ConversationId,
    Guid KnowledgeBaseId,
    Guid TenantId) : ICommand<Guid>;
