using AgentPlatform.Domain.Aggregates.Debug;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Persistence and query operations for <see cref="DebugSession"/> aggregates (F25).
/// </summary>
public interface IDebugSessionRepository
{
    /// <summary>Retrieves a debug session by id.</summary>
    Task<DebugSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Adds a new debug session.</summary>
    void Add(DebugSession session);

    /// <summary>Updates an existing debug session.</summary>
    void Update(DebugSession session);
}
