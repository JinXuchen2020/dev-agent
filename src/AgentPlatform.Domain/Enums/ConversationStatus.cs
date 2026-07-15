namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a conversation between agents and users.
/// </summary>
public enum ConversationStatus
{
    /// <summary>The conversation is active and accepting new messages.</summary>
    Active,

    /// <summary>The conversation has been closed and no longer accepts messages.</summary>
    Closed,

    /// <summary>The conversation has been archived for historical reference.</summary>
    Archived
}
