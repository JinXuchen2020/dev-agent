using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentRepository"/> for persisting and querying agent aggregates.
/// </summary>
internal sealed class AgentRepository : IAgentRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRepository"/> class.
    /// </summary>
    /// <param name="context">The application database context used for data access.</param>
    public AgentRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves an agent by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the agent.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="Agent"/>, or <c>null</c> if not found.</returns>
    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Agents.FindAsync([id], ct);
    }

    /// <summary>
    /// Retrieves all agents belonging to the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter agents by.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of agents for the tenant.</returns>
    public async Task<IReadOnlyList<Agent>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Agents
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves all agents matching the specified role code.
    /// </summary>
    /// <param name="roleCode">The role code to filter by.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of agents with the matching role code.</returns>
    public async Task<IReadOnlyList<Agent>> GetByRoleAsync(string roleCode, CancellationToken ct = default)
    {
        return await _context.Agents
            .Where(a => a.Role.RoleCode == roleCode)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Counts the agents belonging to a specific tenant that have the specified role code.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter by.</param>
    /// <param name="roleCode">The role code to filter by.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the count of matching agents for the tenant.</returns>
    public Task<int> CountByRoleAsync(Guid tenantId, string roleCode, CancellationToken ct = default)
    {
        return _context.Agents
            .CountAsync(a => a.TenantId == tenantId && a.Role.RoleCode == roleCode, ct);
    }

    /// <summary>
    /// Adds a new agent aggregate to the change tracker.
    /// </summary>
    /// <param name="agent">The agent aggregate to add.</param>
    public void Add(Agent agent)
    {
        _context.Agents.Add(agent);
    }

    /// <summary>
    /// Marks the specified agent aggregate as modified so it is updated on the next save.
    /// </summary>
    /// <param name="agent">The agent aggregate to update.</param>
    public void Update(Agent agent)
    {
        _context.Agents.Update(agent);
    }

    /// <summary>
    /// Marks the specified agent aggregate for deletion on the next save.
    /// </summary>
    /// <param name="agent">The agent aggregate to remove.</param>
    public void Remove(Agent agent)
    {
        _context.Agents.Remove(agent);
    }
}
