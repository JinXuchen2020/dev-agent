using AgentPlatform.Domain.Aggregates.HumanApprovals;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 提供 <see cref="HumanApproval"/> 聚合根的持久化与查询操作（租户隔离由仓储调用方保证）。
/// </summary>
public interface IHumanApprovalRepository
{
    /// <summary>按标识符加载审批记录（租户隔离由 AppDbContext 查询过滤器强制）。</summary>
    Task<HumanApproval?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>查找某工作流某节点当前处于 Pending 的审批（用于避免重复创建）。</summary>
    Task<HumanApproval?> GetPendingByNodeAsync(
        Guid tenantId, Guid workflowId, string nodeName, CancellationToken ct = default);

    /// <summary>列出某租户某工作流的全部审批记录（按创建时间倒序）。</summary>
    Task<IReadOnlyList<HumanApproval>> GetByWorkflowAsync(
        Guid tenantId, Guid workflowId, CancellationToken ct = default);

    /// <summary>新增审批记录。</summary>
    void Add(HumanApproval approval);

    /// <summary>更新审批记录（解析后调用）。</summary>
    void Update(HumanApproval approval);
}
