using AgentPlatform.Domain.Aggregates.Workflows;

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
