using AgentPlatform.Domain.Aggregates.Workspaces;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 工作空间成员仓储（F35，决策 D3=B）。查询自动受 AppDbContext 的 <see cref="ITenantScoped"/>
/// 全局过滤器约束。
/// </summary>
public interface IWorkspaceMemberRepository
{
    /// <summary>判断用户是否为指定工作空间的成员。</summary>
    Task<bool> IsMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>列出用户在当前租户内已加入的工作空间 Id 集合（可见性过滤用）。</summary>
    Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>列出指定工作空间的全部成员。</summary>
    Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>统计指定工作空间的成员数（删除守卫用：非空 → 409）。</summary>
    Task<int> CountByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>新增成员关联。</summary>
    Task AddAsync(WorkspaceMember member, CancellationToken ct = default);

    /// <summary>移除成员关联；返回是否确有删除。</summary>
    Task<bool> RemoveAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}
