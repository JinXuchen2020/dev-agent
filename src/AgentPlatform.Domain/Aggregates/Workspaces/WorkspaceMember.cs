using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Workspaces;

/// <summary>
/// 工作空间成员关联（F35，决策 D3=B）。把租户内的用户分配到某个工作空间：
/// 非 Admin 用户仅可见 / 可切换自己所在的（以及默认）工作空间，Admin 管理成员分配。
/// 遵循租户隔离（<see cref="ITenantScoped"/>），但<b>不</b>实现 <see cref="IWorkspaceScoped"/>——
/// <see cref="WorkspaceId"/> 在此是关联数据而非隔离范围。
/// </summary>
public sealed class WorkspaceMember : ITenantScoped
{
    /// <summary>获取成员关联的唯一标识符（ValueGeneratedNever，显式提供）。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取拥有该关联的租户标识符（租户隔离键）。</summary>
    public Guid TenantId { get; private init; }

    /// <summary>获取关联的工作空间标识符。</summary>
    public Guid WorkspaceId { get; private init; }

    /// <summary>获取关联的用户标识符。</summary>
    public Guid UserId { get; private init; }

    /// <summary>获取分配的 UTC 时间。</summary>
    public DateTime CreatedAt { get; private init; }

    private WorkspaceMember() { }

    /// <summary>
    /// 初始化一条工作空间成员关联。
    /// </summary>
    public WorkspaceMember(Guid id, Guid tenantId, Guid workspaceId, Guid userId)
    {
        Id = id;
        TenantId = tenantId;
        WorkspaceId = workspaceId;
        UserId = userId;
        CreatedAt = DateTime.UtcNow;
    }
}
