using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

internal sealed class WorkflowVersionRepository : IWorkflowVersionRepository
{
    private readonly AppDbContext _context;

    public WorkflowVersionRepository(AppDbContext context) => _context = context;

    public async Task<WorkflowVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.WorkflowVersions.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<WorkflowVersion>> ListByWorkflowAsync(
        Guid workflowId, int skip = 0, int take = 20, CancellationToken ct = default) =>
        (await _context.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct)).AsReadOnly();

    public async Task<int> CountByWorkflowAsync(Guid workflowId, CancellationToken ct = default) =>
        await _context.WorkflowVersions.CountAsync(v => v.WorkflowId == workflowId, ct);

    public async Task<int> GetLatestVersionNumberAsync(Guid workflowId, CancellationToken ct = default) =>
        await _context.WorkflowVersions
            .Where(v => v.WorkflowId == workflowId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

    public void Add(WorkflowVersion version) => _context.WorkflowVersions.Add(version);

    public void Remove(WorkflowVersion version) => _context.WorkflowVersions.Remove(version);
}
