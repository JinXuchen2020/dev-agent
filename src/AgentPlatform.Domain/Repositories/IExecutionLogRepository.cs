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
