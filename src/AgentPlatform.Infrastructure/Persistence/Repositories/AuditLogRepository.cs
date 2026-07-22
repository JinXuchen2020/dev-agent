using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for append-only audit log entries.
/// </summary>
internal sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _context;

    public AuditLogRepository(AppDbContext context)
    {
        _context = context;
    }

    public void Add(AuditLog auditLog)
    {
        _context.Set<AuditLog>().Add(auditLog);
    }

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var query = _context.Set<AuditLog>()
            .Where(a => a.TenantId == tenantId)
            .AsQueryable();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
