using AgentPlatform.Domain.Aggregates.Debug;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDebugSessionRepository"/> (F25).
/// </summary>
internal sealed class DebugSessionRepository : IDebugSessionRepository
{
    private readonly AppDbContext _db;

    public DebugSessionRepository(AppDbContext db) => _db = db;

    /// <inheritdoc/>
    public async Task<DebugSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.DebugSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <inheritdoc/>
    public void Add(DebugSession session) => _db.DebugSessions.Add(session);

    /// <inheritdoc/>
    public void Update(DebugSession session) => _db.DebugSessions.Update(session);
}
