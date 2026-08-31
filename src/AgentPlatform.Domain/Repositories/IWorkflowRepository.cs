using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for <see cref="Workflow"/> aggregate roots.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// Retrieves a workflow by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The workflow if found; otherwise <c>null</c>.</returns>
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 触发器路径专用查询（F35）：仅按租户定位工作流，不受当前工作空间查询过滤器约束。
    /// 后台调度 / 匿名 Webhook scope 的工作空间上下文恒解析为租户默认工作空间，
    /// 若沿用工作空间过滤，非默认工作空间的工作流会被静默跳过（永不触发）。
    /// </summary>
    /// <param name="id">The unique identifier of the workflow.</param>
    /// <param name="tenantId">The owning tenant identifier (cross-tenant guard).</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>The workflow if found within the tenant; otherwise <c>null</c>.</returns>
    Task<Workflow?> GetByIdForTriggerAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all workflows belonging to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="ct">A cancellation token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of workflows for the tenant.</returns>
    Task<IReadOnlyList<Workflow>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Queries workflows with server-side pagination and optional status filter.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter workflows by.</param>
    /// <param name="status">Optional filter by workflow state.</param>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Number of records to take.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A tuple with the paginated items and total count.</returns>
    Task<(IReadOnlyList<Workflow> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        WorkflowState? status = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a new workflow to the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to add.</param>
    void Add(Workflow workflow);

    /// <summary>
    /// Updates an existing workflow in the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate with modified state.</param>
    void Update(Workflow workflow);

    /// <summary>
    /// Removes a workflow from the repository.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to remove.</param>
    void Remove(Workflow workflow);
}
