using AgentPlatform.Domain.Aggregates.AuditLogs;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence and query operations for audit log entries.
/// Append-only — no delete method is exposed.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Records an audit log entry.
    /// </summary>
    void Add(AuditLog auditLog);

    /// <summary>
    /// Retrieves paginated audit logs for a tenant, ordered by creation date descending.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> QueryAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);
}
