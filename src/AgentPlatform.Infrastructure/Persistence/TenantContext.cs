using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Scoped 租户上下文持有器。后台任务（调度器）/ 匿名 Webhook 等非 HTTP 请求场景中，
/// 调用方在 DI scope 内设置 <see cref="OverrideTenantId"/>，<see cref="TenantProvider"/>
/// 即优先以此为租户解析来源；HTTP 请求下此值为 null，<see cref="TenantProvider"/> 回退到
/// 请求声明解析，行为不变。
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid? OverrideTenantId { get; set; }
}
