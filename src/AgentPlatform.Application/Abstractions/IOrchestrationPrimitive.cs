using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// The single orchestration primitive for the entire platform (Blueprint C.2).
/// All collaboration modes (sequential / negotiation) are presets of this engine.
/// </summary>
public interface IOrchestrationPrimitive
{
    /// <summary>
    /// Runs a workflow using the configured preset.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to execute.</param>
    /// <param name="preset">The orchestration preset to use.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final state of the workflow after execution.</returns>
    Task<Workflow> RunAsync(Workflow workflow, OrchestrationPreset preset, CancellationToken ct = default);

    /// <summary>
    /// Pauses a running workflow at its current step.
    /// </summary>
    Task PauseAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused workflow from the last incomplete step.
    /// </summary>
    Task<Workflow> ResumeAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Retries a specific step within a workflow.
    /// </summary>
    Task RetryStepAsync(Guid workflowId, int stepOrder, CancellationToken ct = default);

    /// <summary>
    /// Rolls the workflow back to a specified step (precise target, not full reset).
    /// </summary>
    Task RollbackToAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default);

    /// <summary>
    /// Gets a snapshot of the workflow's current execution state from persistent storage.
    /// </summary>
    Task<WorkflowStateSnapshot> GetStateAsync(Guid workflowId, CancellationToken ct = default);
}

/// <summary>
/// Represents a point-in-time snapshot of a workflow's overall state.
/// </summary>
/// <param name="WorkflowId">The unique identifier of the workflow.</param>
/// <param name="CurrentState">The current state of the workflow (e.g., Running, Paused, Completed).</param>
/// <param name="CurrentStepOrder">The zero-based order index of the step currently being executed.</param>
/// <param name="Steps">A read-only list of snapshots for each step in the workflow.</param>
public record WorkflowStateSnapshot(
    Guid WorkflowId,
    WorkflowState CurrentState,
    int CurrentStepOrder,
    IReadOnlyList<StepSnapshot> Steps);

/// <summary>
/// Represents a snapshot of a single workflow step's execution state.
/// </summary>
/// <param name="StepId">The unique identifier of the step.</param>
/// <param name="Order">The zero-based execution order of the step within the workflow.</param>
/// <param name="StepName">The human-readable name of the step.</param>
/// <param name="State">The current state of the step.</param>
/// <param name="Result">The result produced by the step, if any.</param>
/// <param name="ErrorDetail">Detailed error information if the step failed, otherwise <c>null</c>.</param>
public record StepSnapshot(
    Guid StepId,
    int Order,
    string StepName,
    WorkflowState State,
    string? Result,
    string? ErrorDetail);
