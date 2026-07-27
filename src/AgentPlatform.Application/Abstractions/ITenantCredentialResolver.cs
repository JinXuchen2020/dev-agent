using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Resolves a tenant's stored credential settings for a given category (Model / Search).
/// A tenant may hold multiple credentials per category (e.g. several BYO models), so this returns
/// a list. Returns an empty list when the tenant has not configured any BYO credential of that category,
/// signalling the caller to fall back to platform defaults. The returned entities hold only ciphertext — never plaintext.
/// </summary>
public interface ITenantCredentialResolver
{
    /// <summary>Resolves all credential settings for the tenant + category (empty list if none configured).</summary>
    Task<IReadOnlyList<TenantCredentialSetting>> ResolveAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default);

    /// <summary>Invalidates any cached resolution for the tenant + category (called after a create/update/delete).</summary>
    void Invalidate(Guid tenantId, CredentialCategory category);
}
