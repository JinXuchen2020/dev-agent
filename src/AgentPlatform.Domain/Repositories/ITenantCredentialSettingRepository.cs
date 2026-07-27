using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Persistence operations for tenant credential settings.
/// The query filter (centralized in <c>AppDbContext.OnModelCreating</c>) guarantees tenant isolation.
/// </summary>
public interface ITenantCredentialSettingRepository
{
    /// <summary>Retrieves the credential setting for a tenant + category, or null if not configured.</summary>
    Task<TenantCredentialSetting?> GetByTenantAndCategoryAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new setting or updates the existing one for the same tenant + category (upsert).
    /// </summary>
    Task UpsertAsync(TenantCredentialSetting setting, CancellationToken ct = default);
}
