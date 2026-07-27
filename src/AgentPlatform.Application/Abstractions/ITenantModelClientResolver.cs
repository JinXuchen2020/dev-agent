using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Resolves the per-tenant LLM model clients (built from the tenant's BYO credentials).
/// A tenant may configure multiple BYO model credentials, so this returns a list of resolutions —
/// an empty list means the tenant uses platform models. This is the core of per-tenant model isolation.
/// </summary>
public interface ITenantModelClientResolver
{
    /// <summary>
    /// Resolves all of the tenant's enabled model clients. Returns an empty list when the tenant has no
    /// active BYO model credentials, in which case the caller should fall back to the platform model catalog.
    /// </summary>
    Task<IReadOnlyList<TenantModelResolution>> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
