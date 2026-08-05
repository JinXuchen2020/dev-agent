using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IWorkflowTemplateRepository"/> 的 EF Core 实现（F23）。
/// 模板为平台级共享资源，<b>不受</b> AppDbContext 的 <see cref="ITenantScoped"/> 全局过滤器约束
/// （<see cref="WorkflowTemplate"/> 未实现该接口）；所有查询均为全局、对所有租户可见。
/// </summary>
internal sealed class WorkflowTemplateRepository : IWorkflowTemplateRepository
{
    private readonly AppDbContext _context;

    public WorkflowTemplateRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<WorkflowTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<WorkflowTemplate>> ListAsync(
        WorkflowTemplateCategory? category = null,
        string? keyword = null,
        CancellationToken ct = default)
    {
        var query = _context.WorkflowTemplates.AsQueryable();

        if (category.HasValue)
            query = query.Where(t => t.Category == category.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(t =>
                t.Name.Contains(k) ||
                (t.Description != null && t.Description.Contains(k)) ||
                (t.TagsJson != null && EF.Functions.Like(t.TagsJson, $"%{k}%")));
        }

        return await query.OrderBy(t => t.Category).ThenBy(t => t.Name).ToListAsync(ct);
    }

    public void Add(WorkflowTemplate template) =>
        _context.WorkflowTemplates.Add(template);
}
