using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="ExecutionLog"/> aggregate roots.
/// </summary>
public interface IExecutionLogRepository
{
    /// <summary>
    /// Retrieves an execution log by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the execution log.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The execution log if found; otherwise <c>null</c>.</returns>
    Task<ExecutionLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all execution logs for a given workflow.
    /// </summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A read-only list of execution logs for the workflow.</returns>
    Task<IReadOnlyList<ExecutionLog>> GetByWorkflowIdAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>
    /// Queries execution logs with optional filters.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter by.</param>
    /// <param name="status">Optional status to filter by.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Maximum number of records to return.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A tuple with the filtered results and total count.</returns>
    Task<(IReadOnlyList<ExecutionLog> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        WorkflowState? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Queries execution log entries (steps) with server-side pagination and optional status filter.
    /// Queries the entries table directly without loading the parent aggregate.
    /// Returns <c>null</c> if the parent <see cref="ExecutionLog"/> does not exist.
    /// </summary>
    /// <param name="executionLogId">The execution log identifier to filter entries by.</param>
    /// <param name="status">Optional filter by step status.</param>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Number of records to take.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A tuple with the paginated entries and total count, or <c>null</c> if the execution log does not exist.</returns>
    Task<(IReadOnlyList<ExecutionLogEntry> Items, int TotalCount)?> QueryStepsAsync(
        Guid executionLogId,
        WorkflowState? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a new execution log to the repository.
    /// </summary>
    /// <param name="log">The execution log aggregate to add.</param>
    void Add(ExecutionLog log);

    /// <summary>
    /// Updates an existing execution log.
    /// </summary>
    /// <param name="log">The execution log with modified state.</param>
    void Update(ExecutionLog log);
}
