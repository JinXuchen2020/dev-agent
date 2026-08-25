namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Type of an inter-agent message exchanged over the agent message bus (F32).
/// </summary>
public enum AgentMessageType
{
    /// <summary>A completed proposal produced by a proposing agent.</summary>
    Proposal = 0,

    /// <summary>Review feedback about one or more proposals (produced by the critic flow).</summary>
    Critique = 1,

    /// <summary>Task handoff: the receiving agent takes over with the carried context payload.</summary>
    Handoff = 2,

    /// <summary>Orchestration/system-level notification (round markers, circuit-break events).</summary>
    System = 3
}