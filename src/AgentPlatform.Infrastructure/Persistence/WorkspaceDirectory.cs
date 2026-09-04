using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// 租户默认工作空间目录的单例实现（F35）。ConcurrentDictionary 保存 tenantId → defaultWorkspaceId；
/// 由 <c>DatabaseInitializer</c> 启动预载、<c>WorkspaceProvisioner</c> 运行期补齐（新租户 /
/// 集成测试种子）。纯内存同步查询，可在 DbContext 构造期安全调用。
/// </summary>
internal sealed class WorkspaceDirectory : IWorkspaceDirectory
{
    private readonly ConcurrentDictionary<Guid, Guid> _defaults = new();

    /// <inheritdoc />
    public Guid? GetDefaultWorkspaceId(Guid tenantId) =>
        _defaults.TryGetValue(tenantId, out var workspaceId) ? workspaceId : null;

    /// <inheritdoc />
    public void RegisterDefault(Guid tenantId, Guid workspaceId) =>
        _defaults[tenantId] = workspaceId;
}
