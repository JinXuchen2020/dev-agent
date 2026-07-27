using AgentPlatform.Domain.Aggregates.TenantCredentials;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Resolves a tenant's stored credential setting for a given category (Model / Search).
/// Returns null when the tenant has not configured a BYO credential, signalling the caller to
/// fall back to platform defaults. The returned entity holds only ciphertext — never plaintext.
/// </summary>
public interface ITenantCredentialResolver
{
    /// <summary>Resolves the credential setting for the tenant + category, or null if not configured.</summary>
    Task<TenantCredentialSetting?> ResolveAsync(
        Guid tenantId, CredentialCategory category, CancellationToken ct = default);

    /// <summary>Invalidates any cached resolution for the tenant + category (called after a PUT).</summary>
    void Invalidate(Guid tenantId, CredentialCategory category);
}
