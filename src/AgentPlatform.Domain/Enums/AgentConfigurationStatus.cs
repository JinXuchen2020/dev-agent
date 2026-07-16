namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of an agent configuration definition.
/// </summary>
public enum AgentConfigurationStatus
{
    /// <summary>
    /// The configuration is a draft and not yet ready for use.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// The configuration is active and can be used by agents.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The configuration has been archived and is no longer in active use.
    /// </summary>
    Archived = 2,

    /// <summary>
    /// The configuration is deprecated and should be migrated to a newer version.
    /// </summary>
    Deprecated = 3
}
