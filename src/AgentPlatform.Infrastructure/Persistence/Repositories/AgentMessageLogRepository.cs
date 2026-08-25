using AgentPlatform.Domain.Aggregates.AgentMessages;
using AgentPlatform.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentMessageLogRepository"/> (F32 durable message log).
/// Idempotent consumption uses a conditional UPDATE (ConsumedAt IS NULL) so concurrent or
/// repeated consumers get exactly one "you process it" signal per message.
/// </summary>
internal sealed class AgentMessageLogRepository : IAgentMessageLogRepository
{
    private readonly AppDbContext _context;

    public AgentMessageLogRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsAsync(Guid messageId, CancellationToken ct = default) =>
        _context.Set<AgentMessageLog>().AnyAsync(m => m.Id == messageId, ct);

    public void Add(AgentMessageLog message) =>
        _context.Set<AgentMessageLog>().Add(message);

    public void Update(AgentMessageLog message) =>
        _context.Set<AgentMessageLog>().Update(message);

    /// <inheritdoc />
    public async Task<bool> TryMarkConsumedAsync(Guid messageId, CancellationToken ct = default)
    {
        // Conditional update: only the first consumer flips ConsumedAt; affected rows == 0 means
        // another consumer already won — the caller must skip processing (idempotency gate).
        var affected = await _context.Set<AgentMessageLog>()
            .Where(m => m.Id == messageId && m.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ConsumedAt, DateTime.UtcNow), ct);
        return affected == 1;
    }

    public async Task<IReadOnlyList<AgentMessageLog>> GetUnconsumedByWorkflowAsync(Guid workflowId, CancellationToken ct = default) =>
        await _context.Set<AgentMessageLog>()
            .Where(m => m.WorkflowId == workflowId && m.ConsumedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AgentMessageLog>> GetByWorkflowAsync(Guid workflowId, CancellationToken ct = default) =>
        await _context.Set<AgentMessageLog>()
            .Where(m => m.WorkflowId == workflowId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
}