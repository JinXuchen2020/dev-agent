using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRunningExecutionRepository"/> for persisting and querying
/// in-flight workflow executions used by the durable scheduler (F30).
/// </summary>
internal sealed class RunningExecutionRepository : IRunningExecutionRepository
{
    private readonly AppDbContext _context;

    public RunningExecutionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RunningExecution?> GetByWorkflowIdAsync(Guid workflowId, CancellationToken ct = default)
    {
        return await _context.Set<RunningExecution>()
            .FirstOrDefaultAsync(x => x.Id == workflowId, ct);
    }

    public async Task<IReadOnlyList<RunningExecution>> GetExpiredLeasesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Set<RunningExecution>()
            .Where(x => x.TenantId == tenantId
                     && x.WorkflowState == AgentPlatform.Domain.Enums.WorkflowState.Running
                     && x.LeaseExpiresAt < now)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RunningExecution>> GetRunningAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Set<RunningExecution>()
            .Where(x => x.TenantId == tenantId
                     && x.WorkflowState == AgentPlatform.Domain.Enums.WorkflowState.Running)
            .ToListAsync(ct);
    }

    public void Add(RunningExecution execution)
    {
        _context.Set<RunningExecution>().Add(execution);
    }

    public void Update(RunningExecution execution)
    {
        _context.Set<RunningExecution>().Update(execution);
    }

    public void Remove(RunningExecution execution)
    {
        _context.Set<RunningExecution>().Remove(execution);
    }
}