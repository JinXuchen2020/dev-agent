using System.Linq.Expressions;
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExecutionLog>> GetByTenantAsync(
        Guid tenantId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _context.Set<ExecutionLog>()
            .Include(x => x.Entries)
            .Where(x => x.TenantId == tenantId)
            .AsQueryable();

        if (from.HasValue)
            query = query.Where(x => x.StartedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(x => x.StartedAt <= to.Value);

        return await query.OrderByDescending(x => x.StartedAt).ToListAsync(ct);
    }

    /// <summary>
    /// Queries execution log entries (steps) with server-side pagination and optional status filter.
    /// Queries the <c>ExecutionLogEntries</c> table directly without loading the parent aggregate.
    /// Returns <c>null</c> if the parent <see cref="ExecutionLog"/> does not exist.
    /// </summary>
    public async Task<(IReadOnlyList<ExecutionLogEntry> Items, int TotalCount)?> QueryStepsAsync(
        Guid executionLogId,
        WorkflowState? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        // Lightweight existence check (no Include of Entries)
        var logExists = await _context.Set<ExecutionLog>()
            .AnyAsync(x => x.Id == executionLogId, ct);

        if (!logExists)
            return null;

        // Query the ExecutionLogEntries table directly via the owned entity's shadow FK
        var query = _context.Set<ExecutionLogEntry>()
            .Where(BuildEntryLogIdFilter(executionLogId))
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.StepOrder)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private static Expression<Func<ExecutionLogEntry, bool>> BuildEntryLogIdFilter(Guid executionLogId)
    {
        return e => EF.Property<Guid>(e, "ExecutionLogId") == executionLogId;
    }
}
