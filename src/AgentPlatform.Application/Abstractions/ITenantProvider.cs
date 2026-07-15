namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides the current tenant identifier for multi-tenant request scoping.
/// </summary>
public interface ITenantProvider
{
    /// <summary>
    /// Returns the tenant identifier associated with the current request context.
    /// </summary>
    /// <returns>The unique identifier of the current tenant.</returns>
    Guid GetTenantId();
}
