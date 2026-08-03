using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IConversationWorkflowBindingRepository"/> 的 EF Core 实现。
/// 租户隔离由 AppDbContext 的全局 <see cref="ITenantScoped"/> 查询过滤器强制，
/// 所有查询自动限定当前租户。
/// </summary>
internal sealed class ConversationWorkflowBindingRepository : IConversationWorkflowBindingRepository
{
    private readonly AppDbContext _db;

    public ConversationWorkflowBindingRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationWorkflowBinding>> GetByConversationAsync(
        Guid conversationId, CancellationToken ct = default)
        => await _db.ConversationWorkflowBindings
            .Where(b => b.ConversationId == conversationId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<ConversationWorkflowBinding?> GetAsync(
        Guid conversationId, Guid workflowId, CancellationToken ct = default)
        => await _db.ConversationWorkflowBindings
            .FirstOrDefaultAsync(b => b.ConversationId == conversationId && b.WorkflowId == workflowId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationWorkflowBinding>> GetByWorkflowAsync(
        Guid workflowId, CancellationToken ct = default)
        => await _db.ConversationWorkflowBindings
            .Where(b => b.WorkflowId == workflowId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public void Add(ConversationWorkflowBinding binding) => _db.ConversationWorkflowBindings.Add(binding);

    /// <inheritdoc />
    public void Remove(ConversationWorkflowBinding binding) => _db.ConversationWorkflowBindings.Remove(binding);
}
