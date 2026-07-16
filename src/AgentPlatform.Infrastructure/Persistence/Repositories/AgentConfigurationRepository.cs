using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentConfigurationRepository"/> for persisting and querying agent configurations.
/// </summary>
internal sealed class AgentConfigurationRepository : IAgentConfigurationRepository
{
    private readonly AppDbContext _context;

    public AgentConfigurationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AgentConfiguration?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<AgentConfiguration>().FindAsync([id], ct);
    }

    public async Task<(IReadOnlyList<AgentConfiguration> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        AgentConfigurationStatus? status = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default)
    {
        var query = _context.Set<AgentConfiguration>()
            .Where(x => x.TenantId == tenantId);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Skip(skip)
            .Take(Math.Min(take, 100))
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<AgentConfiguration>> GetByAgentTypeCodeAsync(
        string agentTypeCode, CancellationToken ct = default)
    {
        var items = await _context.Set<AgentConfiguration>()
            .Where(x => x.AgentTypeCode == agentTypeCode)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
        return items;
    }

    public void Add(AgentConfiguration configuration)
    {
        _context.Set<AgentConfiguration>().Add(configuration);
    }

    public void Update(AgentConfiguration configuration)
    {
        _context.Set<AgentConfiguration>().Update(configuration);
    }

    public void Remove(AgentConfiguration configuration)
    {
        _context.Set<AgentConfiguration>().Remove(configuration);
    }
}
