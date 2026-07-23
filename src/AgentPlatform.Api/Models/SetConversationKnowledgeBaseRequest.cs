using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API request payload for linking a conversation to a knowledge base.
/// </summary>
/// <param name="KnowledgeBaseId">The unique identifier of the knowledge base to attach.</param>
public record SetConversationKnowledgeBaseRequest(
    [Required] Guid KnowledgeBaseId);
