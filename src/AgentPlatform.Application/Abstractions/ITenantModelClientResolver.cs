using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Resolves the per-tenant LLM model client (built from the tenant's BYO credential) or null when the
/// tenant uses platform models. This is the core of per-tenant model isolation.
/// </summary>
public interface ITenantModelClientResolver
{
    /// <summary>
    /// Resolves the tenant's model client. Returns null when the tenant has no active BYO model credential,
    /// in which case the caller should fall back to the platform model catalog.
    /// </summary>
    Task<TenantModelResolution?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
