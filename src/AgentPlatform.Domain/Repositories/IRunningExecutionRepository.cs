using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="RunningExecution"/> aggregate roots.
/// Used by the durable <see cref="WorkflowScheduler"/> and <see cref="IOrchestrationPrimitive"/> for lease coordination and crash recovery.
/// </summary>
public interface IRunningExecutionRepository
{
    /// <summary>
    /// Retrieves a running execution by its workflow identifier.
    /// </summary>
    Task<RunningExecution?> GetByWorkflowIdAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all running executions for a tenant that are in Running state and have expired leases.
    /// Used by the scheduler to find crashed/stalled executions to recover.
    /// </summary>
    Task<IReadOnlyList<RunningExecution>> GetExpiredLeasesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all running executions for a tenant in Running state (for diagnostics/monitoring).
    /// </summary>
    Task<IReadOnlyList<RunningExecution>> GetRunningAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new running execution to the repository.
    /// </summary>
    void Add(RunningExecution execution);

    /// <summary>
    /// Updates an existing running execution in the repository.
    /// </summary>
    void Update(RunningExecution execution);

    /// <summary>
    /// Removes a running execution from the repository (e.g., after workflow completes).
    /// </summary>
    void Remove(RunningExecution execution);
}