using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IHumanApprovalRepository"/> 的 EF Core 实现。租户隔离由 AppDbContext 的
/// 全局 <see cref="ITenantScoped"/> 查询过滤器强制，所有查询自动限定当前租户。
/// </summary>
internal sealed class HumanApprovalRepository : IHumanApprovalRepository
{
    private readonly AppDbContext _db;

    public HumanApprovalRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HumanApproval?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.HumanApprovals.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<HumanApproval?> GetPendingByNodeAsync(
        Guid tenantId, Guid workflowId, string nodeName, CancellationToken ct = default)
        => await _db.HumanApprovals
            .Where(a => a.TenantId == tenantId
                        && a.WorkflowId == workflowId
                        && a.NodeName == nodeName
                        && a.Status == HumanApprovalStatus.Pending)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<HumanApproval>> GetByWorkflowAsync(
        Guid tenantId, Guid workflowId, CancellationToken ct = default)
        => await _db.HumanApprovals
            .Where(a => a.TenantId == tenantId && a.WorkflowId == workflowId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public void Add(HumanApproval approval) => _db.HumanApprovals.Add(approval);

    public void Update(HumanApproval approval) => _db.HumanApprovals.Update(approval);
}
