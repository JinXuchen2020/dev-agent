using AgentPlatform.Domain.Aggregates.Agents;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="Agent"/> aggregate roots.
/// </summary>
public interface IAgentRepository
{
    /// <summary>
    /// Retrieves an agent by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the agent.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The agent if found; otherwise <c>null</c>.</returns>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all agents belonging to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of agents for the tenant.</returns>
    Task<IReadOnlyList<Agent>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all agents that have the specified role code.
    /// </summary>
    /// <param name="roleCode">The role code to filter by.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of agents matching the role code.</returns>
    Task<IReadOnlyList<Agent>> GetByRoleAsync(string roleCode, CancellationToken ct = default);

    /// <summary>
    /// Adds a new agent to the repository.
    /// </summary>
    /// <param name="agent">The agent aggregate to add.</param>
    void Add(Agent agent);

    /// <summary>
    /// Updates an existing agent in the repository.
    /// </summary>
    /// <param name="agent">The agent aggregate with modified state.</param>
    void Update(Agent agent);

    /// <summary>
    /// Removes an agent from the repository.
    /// </summary>
    /// <param name="agent">The agent aggregate to remove.</param>
    void Remove(Agent agent);
}
