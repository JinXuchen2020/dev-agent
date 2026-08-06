namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Lifecycle states of a workflow debug session (F25).
/// Mirrors the meaningful subset of <see cref="WorkflowState"/> used while stepping
/// through a workflow in debug mode.
/// </summary>
public enum DebugSessionStatus
{
    /// <summary>A session has been created but no node has executed yet.</summary>
    Initialized = 0,

    /// <summary>The session is mid-step (a node is being/has been executed, more remain).</summary>
    Running = 1,

    /// <summary>Stepping paused after a node (manual continue required).</summary>
    Paused = 2,

    /// <summary>All nodes executed successfully.</summary>
    Completed = 3,

    /// <summary>A node failed and the workflow rolled back.</summary>
    Failed = 4,

    /// <summary>The session was rolled back to an earlier step.</summary>
    RolledBack = 5
}
