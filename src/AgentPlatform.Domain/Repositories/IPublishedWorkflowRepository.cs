using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// 已发布工作流仓储（F22）。查询自动受 AppDbContext 的 <see cref="ITenantScoped"/> 全局过滤器约束，
/// 故按 slug 查询只返回当前租户记录。
/// </summary>
public interface IPublishedWorkflowRepository
{
    /// <summary>按公开 slug 查找（受当前租户过滤器约束）。</summary>
    Task<PublishedWorkflow?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>按工作流标识符查找该租户下的发布记录（每工作流至多一条）。</summary>
    Task<PublishedWorkflow?> GetByWorkflowIdAsync(Guid tenantId, Guid workflowId, CancellationToken ct = default);

    /// <summary>列出某租户下指定形态、可选仅启用的发布记录（供 MCP tools/list 使用）。</summary>
    Task<IReadOnlyList<PublishedWorkflow>> GetByTenantAndModeAsync(
        Guid tenantId, PublishMode mode, bool enabledOnly, CancellationToken ct = default);

    /// <summary>新增一条发布记录（由 UnitOfWorkBehavior 统一提交）。</summary>
    void Add(PublishedWorkflow publishedWorkflow);

    /// <summary>删除一条发布记录（由 UnitOfWorkBehavior 统一提交）。</summary>
    void Delete(PublishedWorkflow publishedWorkflow);
}
