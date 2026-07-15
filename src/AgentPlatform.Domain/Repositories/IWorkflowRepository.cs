using AgentPlatform.Domain.Aggregates.Workflows;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="Workflow"/> aggregate roots.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// Retrieves a workflow by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The workflow if found; otherwise <c>null</c>.</returns>
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all workflows belonging to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of workflows for the tenant.</returns>
    Task<IReadOnlyList<Workflow>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new workflow to the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to add.</param>
    void Add(Workflow workflow);

    /// <summary>
    /// Updates an existing workflow in the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate with modified state.</param>
    void Update(Workflow workflow);

    /// <summary>
    /// Removes a workflow from the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to remove.</param>
    void Remove(Workflow workflow);
}
