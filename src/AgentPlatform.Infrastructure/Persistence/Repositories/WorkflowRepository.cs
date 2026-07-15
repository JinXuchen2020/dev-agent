using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowRepository"/> for persisting and querying workflow aggregates.
/// </summary>
internal sealed class WorkflowRepository : IWorkflowRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowRepository"/> class.
    /// </summary>
    /// <param name="context">The application database context used for data access.</param>
    public WorkflowRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves a workflow by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the workflow.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the matching <see cref="Workflow"/>, or <c>null</c> if not found.</returns>
    public async Task<Workflow?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Workflows.FindAsync([id], ct);
    }

    /// <summary>
    /// Retrieves all workflows belonging to the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter workflows by.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with a read-only list of workflows for the tenant.</returns>
    public async Task<IReadOnlyList<Workflow>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.Workflows
            .Where(w => w.TenantId == tenantId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Adds a new workflow aggregate to the change tracker.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to add.</param>
    public void Add(Workflow workflow)
    {
        _context.Workflows.Add(workflow);
    }

    /// <summary>
    /// Marks the specified workflow aggregate as modified so it is updated on the next save.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to update.</param>
    public void Update(Workflow workflow)
    {
        _context.Workflows.Update(workflow);
    }

    /// <summary>
    /// Marks the specified workflow aggregate for deletion on the next save.
    /// </summary>
    /// <param name="workflow">The workflow aggregate to remove.</param>
    public void Remove(Workflow workflow)
    {
        _context.Workflows.Remove(workflow);
    }
}
