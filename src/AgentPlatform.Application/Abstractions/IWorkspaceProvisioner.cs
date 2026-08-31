namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 工作空间供应器（F35）：为租户幂等补齐默认工作空间（存在即复用，缺失则创建）并登记到
/// <see cref="IWorkspaceDirectory"/>；以及把 <c>WorkspaceId</c> 为空的历史行回填到租户默认工作空间。
/// 由 <c>DatabaseInitializer</c>（启动预载）与集成测试种子（运行期新租户）共用。
/// </summary>
public interface IWorkspaceProvisioner
{
    /// <summary>确保租户存在默认工作空间并登记目录，返回默认工作空间 Id。幂等。</summary>
    Task<Guid> EnsureDefaultWorkspaceAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>把所有 <c>WorkspaceId</c> 为空的存量行回填到各自租户的默认工作空间。幂等。</summary>
    Task BackfillEmptyWorkspaceIdsAsync(CancellationToken ct = default);
}
