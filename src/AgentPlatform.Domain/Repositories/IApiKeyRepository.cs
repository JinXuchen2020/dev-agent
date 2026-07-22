using AgentPlatform.Domain.Aggregates.ApiKeys;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Provides persistence operations for API key aggregates.
/// Supports key lookup for authentication, rotation, and lifecycle management.
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>
    /// Retrieves all active (non-revoked, non-expired) API keys for a tenant,
    /// ordered by creation date descending.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetActiveKeysAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an API key by its identifier.
    /// </summary>
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all active keys across all tenants (for authentication lookup).
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetAllActiveKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves keys that are expiring within the specified number of days from now.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetExpiringKeysAsync(int withinDays, CancellationToken ct = default);

    /// <summary>
    /// Retrieves keys that are already past their expiry timestamp but are still
    /// active and not yet revoked. Used by the expiry job to revoke stale keys.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetExpiredActiveKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new API key to the repository.
    /// </summary>
    void Add(ApiKey apiKey);

    /// <summary>
    /// Updates an existing API key in the repository.
    /// </summary>
    void Update(ApiKey apiKey);

    /// <summary>
    /// Marks an existing API key as updated. Persistence is performed by the
    /// caller's unit of work (see <c>IUnitOfWork.SaveChangesAsync</c>).
    /// </summary>
    Task UpdateAsync(ApiKey apiKey, CancellationToken ct = default);
}
