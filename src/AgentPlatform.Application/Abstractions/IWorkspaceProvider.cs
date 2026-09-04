namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 提供当前工作空间标识符（F35）。解析链与 <see cref="ITenantProvider"/> 同构：
/// <see cref="IWorkspaceContext.OverrideWorkspaceId"/>（后台 / 匿名 scope 注入）→
/// JWT "workspace_id" claim → "X-Workspace-Id" header → 租户默认工作空间
/// （经 <see cref="IWorkspaceDirectory"/> 同步查询）→ <see cref="Guid.Empty"/>（无上下文 = 查询空集，诚实隔离）。
/// </summary>
public interface IWorkspaceProvider
{
    /// <summary>
    /// 返回当前请求上下文应使用的工作空间标识符；无法解析时返回 <see cref="Guid.Empty"/>
    /// （此时 query filter 将不命中任何行，避免静默越权泄漏）。
    /// </summary>
    Guid GetWorkspaceId();
}
