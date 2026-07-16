using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Domain.Aggregates.Conversations;

/// <summary>
/// Represents a single message within a conversation, authored by a user, agent,
/// system, or tool, and tracking its token usage for cost analysis.
/// </summary>
public sealed class Message
{
    /// <summary>
    /// Gets the unique identifier of the message.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the role of the message author (e.g., user, agent, system, or tool).
    /// </summary>
    public MessageRole Role { get; private init; }

    /// <summary>
    /// Gets the textual content of the message.
    /// </summary>
    public string Content { get; private init; }

    /// <summary>
    /// Gets the optional JSON-serialized tool calls associated with the message.
    /// </summary>
    public string? ToolCalls { get; private init; }

    /// <summary>
    /// Gets the optional token usage recorded for this message, if applicable.
    /// </summary>
    public TokenUsage? TokenUsage { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the message was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the message was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Private parameterless constructor for EF Core materialization.
    /// </summary>
    private Message()
    {
        Content = null!; // EF Core proxy
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Message"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the message.</param>
    /// <param name="role">The role of the message author.</param>
    /// <param name="content">The textual content of the message.</param>
    /// <param name="toolCalls">The optional JSON-serialized tool calls associated with the message.</param>
    /// <param name="tokenUsage">The optional token usage recorded for this message.</param>
    public Message(Guid id, MessageRole role, string content,
        string? toolCalls = null, TokenUsage? tokenUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Id = id;
        Role = role;
        Content = content;
        ToolCalls = toolCalls;
        TokenUsage = tokenUsage;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }
}
