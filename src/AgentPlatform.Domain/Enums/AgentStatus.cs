namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the operational status of an agent within the platform.
/// </summary>
public enum AgentStatus
{
    /// <summary>The agent is active and available to process tasks.</summary>
    Active,

    /// <summary>The agent is inactive and unavailable for task assignment.</summary>
    Inactive,

    /// <summary>The agent is in an error state and requires intervention.</summary>
    Error
}
