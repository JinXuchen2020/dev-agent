using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 工作流触发器持久化与查询。调度扫描需跨租户（IgnoreQueryFilters），其余查询受
/// AppDbContext 全局 ITenantScoped 过滤器约束。
/// </summary>
public interface IWorkflowTriggerRepository
{
    /// <summary>按 webhook token 查找启用的 Webhook 触发器（跨租户，用于匿名端点鉴权）。</summary>
    Task<WorkflowTrigger?> GetByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>查找某工作流指定类型的触发器（受租户过滤）。</summary>
    Task<WorkflowTrigger?> GetByWorkflowAndTypeAsync(Guid workflowId, TriggerType type, CancellationToken ct = default);

    /// <summary>扫描所有租户中到期（NextRunAt &lt;= nowUtc）且启用的 Schedule 触发器（跨租户，忽略租户过滤器）。</summary>
    Task<IReadOnlyList<WorkflowTrigger>> GetDueSchedulesAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>列出某工作流的全部触发器（受租户过滤）。</summary>
    Task<IReadOnlyList<WorkflowTrigger>> ListByWorkflowAsync(Guid workflowId, CancellationToken ct = default);

    void Add(WorkflowTrigger trigger);

    void Update(WorkflowTrigger trigger);
}
