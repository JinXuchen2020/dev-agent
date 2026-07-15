using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Defines the contract for a state machine engine that orchestrates workflow step execution
/// with support for branching, retry, and rollback.
/// </summary>
public interface IStateMachineEngine
{
    /// <summary>
    /// Starts executing the workflow from the first eligible step.
    /// </summary>
    Task<WorkflowState> StartAsync(Workflow workflow, CancellationToken ct = default);

    /// <summary>
    /// Pauses the workflow at the current step.
    /// </summary>
    Task PauseAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused workflow from the last incomplete step.
    /// </summary>
    Task<WorkflowState> ResumeAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of the workflow.
    /// </summary>
    Task<WorkflowState> GetStatusAsync(Guid workflowId, CancellationToken ct = default);
}
