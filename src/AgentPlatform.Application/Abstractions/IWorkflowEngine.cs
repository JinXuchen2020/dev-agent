using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for managing the lifecycle of a workflow, including starting, pausing, resuming, retrying, and rolling back steps.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Starts execution of the specified workflow.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to start.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    Task StartAsync(Workflow workflow, CancellationToken ct = default);

    /// <summary>
    /// Pauses the workflow identified by the given identifier.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to pause.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous pause operation.</returns>
    Task PauseAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Resumes a previously paused workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to resume.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous resume operation.</returns>
    Task ResumeAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Retries execution of a specific step within a workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow containing the step.</param>
    /// <param name="stepOrder">The zero-based order index of the step to retry.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous retry operation.</returns>
    Task RetryAsync(Guid workflowId, int stepOrder, CancellationToken ct = default);

    /// <summary>
    /// Rolls the workflow back to the state immediately before the specified step.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to roll back.</param>
    /// <param name="targetStepOrder">The order index of the step to roll back to.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous rollback operation.</returns>
    Task RollbackAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a point-in-time snapshot of the workflow's current state.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the workflow state snapshot.</returns>
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
