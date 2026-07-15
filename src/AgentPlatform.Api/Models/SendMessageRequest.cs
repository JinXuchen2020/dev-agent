using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API request payload for sending a message within a conversation.
/// </summary>
/// <param name="Content">The content of the message to send. Required.</param>
/// <param name="SearchQuery">An optional search query used to ground the reply in retrieved context.</param>
/// <param name="Model">An optional model identifier override for this request. When null, the router selects a model.</param>
public record SendMessageRequest(
    [Required] string Content,
    string? SearchQuery = null,
    string? Model = null);
