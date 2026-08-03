using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IPublishedWorkflowRepository"/> 的 EF Core 实现（F22）。
/// 所有查询均受 AppDbContext 的 <see cref="ITenantScoped"/> 全局过滤器约束（自动按当前租户隔离）。
/// </summary>
internal sealed class PublishedWorkflowRepository : IPublishedWorkflowRepository
{
    private readonly AppDbContext _context;

    public PublishedWorkflowRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PublishedWorkflow?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await _context.Set<PublishedWorkflow>().FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public async Task<PublishedWorkflow?> GetByWorkflowIdAsync(Guid tenantId, Guid workflowId, CancellationToken ct = default) =>
        await _context.Set<PublishedWorkflow>().FirstOrDefaultAsync(
            p => p.TenantId == tenantId && p.WorkflowId == workflowId, ct);

    public async Task<IReadOnlyList<PublishedWorkflow>> GetByTenantAndModeAsync(
        Guid tenantId, PublishMode mode, bool enabledOnly, CancellationToken ct = default)
    {
        var query = _context.Set<PublishedWorkflow>().Where(p => p.TenantId == tenantId && p.Mode == mode);
        if (enabledOnly)
            query = query.Where(p => p.IsEnabled);
        return await query.ToListAsync(ct);
    }

    public void Add(PublishedWorkflow publishedWorkflow) =>
        _context.Set<PublishedWorkflow>().Add(publishedWorkflow);

    public void Delete(PublishedWorkflow publishedWorkflow) =>
        _context.Set<PublishedWorkflow>().Remove(publishedWorkflow);
}
