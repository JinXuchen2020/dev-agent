namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the role of a participant in a conversation message.
/// </summary>
public enum MessageRole
{
    /// <summary>The message was authored by an end user.</summary>
    User,

    /// <summary>The message was authored by an agent.</summary>
    Agent,

    /// <summary>The message is a system-level instruction or context.</summary>
    System,

    /// <summary>The message contains the result of a tool invocation.</summary>
    Tool
}
