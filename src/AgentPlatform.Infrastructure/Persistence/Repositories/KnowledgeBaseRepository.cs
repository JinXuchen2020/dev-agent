using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IKnowledgeBaseRepository"/> 的 EF Core 实现。
/// </summary>
internal sealed class KnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly AppDbContext _db;

    /// <summary>初始化 <see cref="KnowledgeBaseRepository"/> 的新实例。</summary>
    public KnowledgeBaseRepository(AppDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<KnowledgeBase?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.KnowledgeBases.Include(k => k.Documents)
            .FirstOrDefaultAsync(k => k.Id == id, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeBase>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await _db.KnowledgeBases.Include(k => k.Documents)
            .Where(k => k.TenantId == tenantId)
            .OrderBy(k => k.Name)
            .ToListAsync(ct);

    /// <inheritdoc/>
    public void Add(KnowledgeBase knowledgeBase) => _db.KnowledgeBases.Add(knowledgeBase);

    /// <inheritdoc/>
    public void Update(KnowledgeBase knowledgeBase) => _db.KnowledgeBases.Update(knowledgeBase);

    /// <inheritdoc/>
    public void Remove(KnowledgeBase knowledgeBase) => _db.KnowledgeBases.Remove(knowledgeBase);
}
