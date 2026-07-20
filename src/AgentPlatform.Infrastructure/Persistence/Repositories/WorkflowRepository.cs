using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
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
    /// Queries workflows with server-side pagination and optional status filter.
    /// Executes filtering, ordering, skip/take, and count all at the database level.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to filter workflows by.</param>
    /// <param name="status">Optional filter by workflow state.</param>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Number of records to take.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A tuple with the paginated items and total count.</returns>
    public async Task<(IReadOnlyList<Workflow> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        WorkflowState? status = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default)
    {
        var query = _context.Workflows
            .Where(w => w.TenantId == tenantId)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(w => w.CurrentState == status.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(w => w.Steps)
            .OrderByDescending(w => w.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
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
