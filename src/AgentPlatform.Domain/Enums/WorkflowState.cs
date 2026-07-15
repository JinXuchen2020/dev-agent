namespace AgentPlatform.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of a workflow execution.
/// </summary>
public enum WorkflowState
{
    /// <summary>The workflow has been created but has not started execution.</summary>
    Pending,

    /// <summary>The workflow is currently executing.</summary>
    Running,

    /// <summary>The workflow execution has been temporarily suspended.</summary>
    Paused,

    /// <summary>The workflow has completed all steps successfully.</summary>
    Completed,

    /// <summary>The workflow execution failed due to an error.</summary>
    Failed,

    /// <summary>The workflow has been rolled back to a previous state.</summary>
    RolledBack
}
