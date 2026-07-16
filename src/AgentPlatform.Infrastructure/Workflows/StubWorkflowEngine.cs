using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Placeholder workflow engine that simulates workflow lifecycle operations without persistent execution.
/// </summary>
[Obsolete("Replaced by OrchestrationPrimitive via IOrchestrationPrimitive (Blueprint C.2).")]
internal sealed class StubWorkflowEngine : IWorkflowEngine
{
    /// <summary>
    /// Starts the specified workflow by transitioning it to the <see cref="WorkflowState.Running"/> state.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to start.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous start operation.</returns>
    public Task StartAsync(Workflow workflow, CancellationToken ct = default)
    {
        workflow.SetState(Domain.Enums.WorkflowState.Running);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses the workflow identified by the supplied identifier.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to pause.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous pause operation.</returns>
    public Task PauseAsync(Guid workflowId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes a previously paused workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to resume.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous resume operation.</returns>
    public Task ResumeAsync(Guid workflowId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retries a specific step within the workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow containing the step.</param>
    /// <param name="stepOrder">The zero-based order index of the step to retry.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous retry operation.</returns>
    public Task RetryAsync(Guid workflowId, int stepOrder, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rolls the workflow back to the specified target step.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to roll back.</param>
    /// <param name="targetStepOrder">The zero-based order index of the step to roll back to.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous rollback operation.</returns>
    public Task RollbackAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves a snapshot of the current state of the specified workflow.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow to inspect.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a <see cref="WorkflowStateSnapshot"/> describing the workflow state.</returns>
    public Task<WorkflowStateSnapshot> GetStateAsync(Guid workflowId, CancellationToken ct = default)
    {
        return Task.FromResult(new WorkflowStateSnapshot(
            workflowId, Domain.Enums.WorkflowState.Pending, 0, []));
    }
}
