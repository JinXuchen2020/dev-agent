using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IExecutionLogRepository"/> for persisting and querying execution logs.
/// </summary>
internal sealed class ExecutionLogRepository : IExecutionLogRepository
{
    private readonly AppDbContext _context;

    public ExecutionLogRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ExecutionLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Set<ExecutionLog>()
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<ExecutionLog>> GetByWorkflowIdAsync(
        Guid workflowId, CancellationToken ct = default)
    {
        return await _context.Set<ExecutionLog>()
            .Include(x => x.Entries)
            .Where(x => x.WorkflowId == workflowId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<ExecutionLog> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        WorkflowState? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default)
    {
        var query = _context.Set<ExecutionLog>()
            .Include(x => x.Entries)
            .Where(x => x.TenantId == tenantId)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (from.HasValue)
            query = query.Where(x => x.StartedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.StartedAt <= to.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public void Add(ExecutionLog log)
    {
        _context.Set<ExecutionLog>().Add(log);
    }

    public void Update(ExecutionLog log)
    {
        _context.Set<ExecutionLog>().Update(log);
    }
}
