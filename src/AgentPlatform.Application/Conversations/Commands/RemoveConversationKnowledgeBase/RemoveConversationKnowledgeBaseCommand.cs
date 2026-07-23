using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Conversations.Commands.RemoveConversationKnowledgeBase;

/// <summary>
/// Unlinks a conversation from any previously attached knowledge base.
/// </summary>
/// <param name="ConversationId">The unique identifier of the conversation to unlink.</param>
/// <param name="TenantId">The current tenant identifier.</param>
public record RemoveConversationKnowledgeBaseCommand(
    Guid ConversationId,
    Guid TenantId) : ICommand<Guid>;
