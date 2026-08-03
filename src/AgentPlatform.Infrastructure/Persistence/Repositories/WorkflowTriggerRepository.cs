using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IWorkflowTriggerRepository"/> 的 EF Core 实现。
/// 租户隔离由 AppDbContext 的全局 <see cref="ITenantScoped"/> 查询过滤器强制；
/// 跨租户查询（匿名 Webhook token 鉴权、调度器到期扫描）显式 <c>IgnoreQueryFilters()</c>。
/// </summary>
internal sealed class WorkflowTriggerRepository : IWorkflowTriggerRepository
{
    private readonly AppDbContext _db;

    public WorkflowTriggerRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<WorkflowTrigger?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.WorkflowTriggers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TriggerToken == token, ct);

    /// <inheritdoc />
    public async Task<WorkflowTrigger?> GetByWorkflowAndTypeAsync(
        Guid workflowId, TriggerType type, CancellationToken ct = default)
        => await _db.WorkflowTriggers
            .FirstOrDefaultAsync(t => t.WorkflowId == workflowId && t.Type == type, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowTrigger>> GetDueSchedulesAsync(
        DateTime nowUtc, CancellationToken ct = default)
        => await _db.WorkflowTriggers
            .IgnoreQueryFilters()
            .Where(t => t.Type == TriggerType.Schedule
                        && t.Enabled
                        && t.NextRunAt != null
                        && t.NextRunAt <= nowUtc)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowTrigger>> ListByWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
        => await _db.WorkflowTriggers
            .Where(t => t.WorkflowId == workflowId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public void Add(WorkflowTrigger trigger) => _db.WorkflowTriggers.Add(trigger);

    /// <inheritdoc />
    public void Update(WorkflowTrigger trigger) => _db.WorkflowTriggers.Update(trigger);
}
