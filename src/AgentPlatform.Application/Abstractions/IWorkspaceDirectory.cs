namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 租户默认工作空间目录（单例，F35）。保存 tenantId → 默认 workspaceId 的内存映射：
/// 由 <c>DatabaseInitializer</c> 在启动时预载、幂等补齐，供 <see cref="IWorkspaceProvider"/>
/// 在无 claim / header 的场景（后台任务、旧令牌）同步解析兜底，避免在 DbContext 构造期查库。
/// </summary>
public interface IWorkspaceDirectory
{
    /// <summary>返回租户的默认工作空间 Id；未登记（未初始化完成 / 未知租户）时返回 null。</summary>
    Guid? GetDefaultWorkspaceId(Guid tenantId);

    /// <summary>登记 / 更新租户默认工作空间 Id（启动预载与运行期补齐共用）。</summary>
    void RegisterDefault(Guid tenantId, Guid workspaceId);
}
