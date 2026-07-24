using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Abstractions;

/// <summary>
/// Unified execution contract shared by <see cref="WorkflowNode"/> (DAG) and
/// <see cref="WorkflowStep"/> (legacy linear). Lets step executors and orchestrators
/// operate on either representation without type branching.
/// </summary>
public interface IWorkflowExecutable
{
    /// <summary>Unique identifier of the executable.</summary>
    Guid Id { get; }

    /// <summary>Display name of the executable (also the agent-assignment key).</summary>
    string Name { get; }

    /// <summary>Zero-based execution order.</summary>
    int Order { get; }

    /// <summary>Current execution state.</summary>
    WorkflowState State { get; }

    /// <summary>Assigned agent identifier, if any.</summary>
    Guid? AssignedAgentId { get; }

    /// <summary>Explicit step type for DAG routing; null for legacy linear steps.</summary>
    StepType? Type { get; }

    /// <summary>Node/step configuration as a JSON string (DAG nodes); "{}" for legacy steps.</summary>
    string ConfigJson { get; }

    /// <summary>Sets the execution state.</summary>
    void SetState(WorkflowState state);

    /// <summary>Sets the result and marks completed.</summary>
    void SetResult(string result);

    /// <summary>Records an error and marks failed.</summary>
    void SetError(string error);

    /// <summary>Result produced on completion, if any.</summary>
    string? Result { get; }

    /// <summary>Error details recorded on failure, if any.</summary>
    string? ErrorDetail { get; }
}
