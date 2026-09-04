using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Infrastructure.Persistence;

/// <summary>
/// Scoped 工作空间上下文持有器（F35，与 <see cref="TenantContext"/> 同构）。
/// 后台任务（调度器）/ 匿名 Webhook 等非 HTTP 请求场景中，调用方在 DI scope 内设置
/// <see cref="OverrideWorkspaceId"/>，<see cref="WorkspaceProvider"/> 即优先以此为解析来源；
/// HTTP 请求下此值为 null，回退到请求声明（JWT claim / header）解析，行为不变。
/// </summary>
internal sealed class WorkspaceContext : IWorkspaceContext
{
    /// <inheritdoc />
    public Guid? OverrideWorkspaceId { get; set; }
}
