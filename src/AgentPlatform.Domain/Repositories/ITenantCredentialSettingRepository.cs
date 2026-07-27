using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Persistence operations for tenant credential settings.
/// A tenant may hold multiple credentials per category (e.g. several different BYO models),
/// so reads return lists keyed by (tenant, category). The query filter (centralized in
/// <c>AppDbContext.OnModelCreating</c>) guarantees tenant isolation.
/// </summary>
public interface ITenantCredentialSettingRepository
{
    /// <summary>Retrieves all credential settings for a tenant + category (may be empty).</summary>
    Task<IReadOnlyList<TenantCredentialSetting>> GetAllByTenantAndCategoryAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default);

    /// <summary>Retrieves a single credential by id, scoped to the tenant, or null if not found.</summary>
    Task<TenantCredentialSetting?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Inserts a new credential setting.</summary>
    Task AddAsync(TenantCredentialSetting setting, CancellationToken ct = default);

    /// <summary>Applies in-place updates to an existing tracked credential (loaded via <see cref="GetByIdAsync"/>).</summary>
    Task UpdateAsync(TenantCredentialSetting setting, CancellationToken ct = default);

    /// <summary>Removes a credential setting.</summary>
    Task DeleteAsync(TenantCredentialSetting setting, CancellationToken ct = default);
}
