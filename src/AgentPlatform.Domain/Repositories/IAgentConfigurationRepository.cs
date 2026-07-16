using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="AgentConfiguration"/> aggregate roots.
/// </summary>
public interface IAgentConfigurationRepository
{
    /// <summary>
    /// Retrieves an agent configuration by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the configuration.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>The configuration if found; otherwise <c>null</c>.</returns>
    Task<AgentConfiguration?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all agent configurations belonging to a specific tenant,
    /// with optional status and pagination filters.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="status">Optional filter by configuration status.</param>
    /// <param name="skip">Number of records to skip (default: 0).</param>
    /// <param name="take">Number of records to take (default: 20, max: 100).</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A tuple containing the list of configurations and the total count.</returns>
    Task<(IReadOnlyList<AgentConfiguration> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        AgentConfigurationStatus? status = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all configurations associated with a specific agent type code.
    /// </summary>
    /// <param name="agentTypeCode">The agent type code to filter by.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A read-only list of matching configurations.</returns>
    Task<IReadOnlyList<AgentConfiguration>> GetByAgentTypeCodeAsync(
        string agentTypeCode, CancellationToken ct = default);

    /// <summary>
    /// Adds a new agent configuration to the repository.
    /// </summary>
    /// <param name="configuration">The configuration aggregate to add.</param>
    void Add(AgentConfiguration configuration);

    /// <summary>
    /// Updates an existing agent configuration in the repository.
    /// </summary>
    /// <param name="configuration">The configuration aggregate with modified state.</param>
    void Update(AgentConfiguration configuration);

    /// <summary>
    /// Removes an agent configuration from the repository.
    /// </summary>
    /// <param name="configuration">The configuration aggregate to remove.</param>
    void Remove(AgentConfiguration configuration);
}
