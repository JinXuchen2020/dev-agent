using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentRoleDefinitionRepository"/> for persisting and querying custom agent role definitions.
/// </summary>
internal sealed class AgentRoleDefinitionRepository : IAgentRoleDefinitionRepository
{
    private readonly AppDbContext _context;

    public AgentRoleDefinitionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AgentRoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<AgentRoleDefinition>().FindAsync([id], ct);
    }

    public Task<AgentRoleDefinition?> GetByRoleCodeAsync(string roleCode, CancellationToken ct = default)
    {
        return _context.Set<AgentRoleDefinition>()
            .FirstOrDefaultAsync(x => x.RoleCode == roleCode, ct);
    }

    public async Task<IReadOnlyList<AgentRoleDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var roles = await _context.Set<AgentRoleDefinition>()
            .OrderBy(x => x.RoleCode)
            .ToListAsync(ct);
        return roles;
    }

    public void Add(AgentRoleDefinition definition)
    {
        _context.Set<AgentRoleDefinition>().Add(definition);
    }

    public void Update(AgentRoleDefinition definition)
    {
        _context.Set<AgentRoleDefinition>().Update(definition);
    }

    public void Remove(AgentRoleDefinition definition)
    {
        _context.Set<AgentRoleDefinition>().Remove(definition);
    }
}
