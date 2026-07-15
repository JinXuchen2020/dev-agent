using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="AgentRoleDefinition"/> aggregates.
/// </summary>
public interface IAgentRoleDefinitionRepository
{
    /// <summary>
    /// Retrieves a role definition by its unique identifier.
    /// </summary>
    Task<AgentRoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a role definition by its role code.
    /// </summary>
    Task<AgentRoleDefinition?> GetByRoleCodeAsync(string roleCode, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all role definitions.
    /// </summary>
    Task<IReadOnlyList<AgentRoleDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new role definition to the repository.
    /// </summary>
    void Add(AgentRoleDefinition definition);

    /// <summary>
    /// Updates an existing role definition in the repository.
    /// </summary>
    void Update(AgentRoleDefinition definition);

    /// <summary>
    /// Removes a role definition from the repository.
    /// </summary>
    void Remove(AgentRoleDefinition definition);
}
