namespace AgentPlatform.Domain.Abstractions;

/// <summary>
/// Defines the contract for entities that are scoped to a specific tenant,
/// enabling multi-tenant isolation throughout the domain.
/// </summary>
public interface ITenantScoped
{
    /// <summary>
    /// Gets the unique identifier of the tenant that owns this entity.
    /// </summary>
    Guid TenantId { get; }
}
