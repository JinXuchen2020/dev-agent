namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 租户上下文（scoped），允许在后台任务 / 匿名 Webhook 等非 HTTP 请求场景中显式注入当前租户。
/// <see cref="ITenantProvider"/> 的实现会优先读取此覆盖值，再回退到 HTTP 上下文声明；
/// HTTP 请求下此值为 null，行为不变。
/// </summary>
public interface ITenantContext
{
    /// <summary>当在后台 / 匿名 scope 中显式设置时，作为租户解析的最高优先级；否则为 null。</summary>
    Guid? OverrideTenantId { get; set; }
}
