namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 工作空间上下文（scoped），允许在后台任务 / 匿名 Webhook 等非 HTTP 请求场景中显式注入当前工作空间。
/// <see cref="IWorkspaceProvider"/> 的实现会优先读取此覆盖值，再回退到 HTTP 上下文（JWT claim / header）；
/// HTTP 请求下此值为 null，行为不变。与 <see cref="ITenantContext"/> 同构。
/// </summary>
public interface IWorkspaceContext
{
    /// <summary>当在后台 / 匿名 scope 中显式设置时，作为工作空间解析的最高优先级；否则为 null。</summary>
    Guid? OverrideWorkspaceId { get; set; }
}
