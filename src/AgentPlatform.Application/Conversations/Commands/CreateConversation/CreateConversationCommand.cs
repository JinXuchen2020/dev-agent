using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Conversations.Commands.CreateConversation;

/// <summary>
/// Represents a command to create a new conversation for the specified tenant.
/// </summary>
/// <param name="TenantId">The unique identifier of the tenant that owns the conversation.</param>
public record CreateConversationCommand(Guid TenantId) : ICommand<Guid>;
