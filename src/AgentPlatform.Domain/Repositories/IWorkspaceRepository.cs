using AgentPlatform.Domain.Aggregates.Workspaces;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 工作空间仓储（F35）。查询自动受 AppDbContext 的 <see cref="ITenantScoped"/> 全局过滤器约束；
/// 删除守卫所需的「工作空间内业务实体计数」由 Infrastructure 侧跨聚合统计。
/// </summary>
public interface IWorkspaceRepository
{
    /// <summary>按 Id 获取工作空间（当前租户内）。</summary>
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>获取当前租户的默认工作空间（恒存在，由 DatabaseInitializer 保证）。</summary>
    Task<Workspace?> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>列出当前租户的全部工作空间。</summary>
    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken ct = default);

    /// <summary>按 Id 集合列出工作空间（当前租户内，用于非 Admin 成员可见性过滤）。</summary>
    Task<IReadOnlyList<Workspace>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>判断同租户内是否已存在同名工作空间（名称唯一约束）。</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);

    /// <summary>新增工作空间。</summary>
    Task AddAsync(Workspace workspace, CancellationToken ct = default);

    /// <summary>删除工作空间聚合（调用方须先通过删除守卫校验）。</summary>
    void Remove(Workspace workspace);

    /// <summary>
    /// 统计工作空间内仍存在的业务实体总数（18 个 <c>IWorkspaceScoped</c> 聚合跨表求和），
    /// 供删除守卫（非空 → 409）使用。
    /// </summary>
    Task<int> CountBusinessEntitiesAsync(Guid workspaceId, CancellationToken ct = default);
}
