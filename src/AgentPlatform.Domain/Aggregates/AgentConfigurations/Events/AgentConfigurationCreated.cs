using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.AgentConfigurations.Events;

/// <summary>
/// Domain event raised when a new agent configuration is created.
/// </summary>
/// <param name="ConfigurationId">The unique identifier of the configuration.</param>
/// <param name="Name">The name of the configuration.</param>
/// <param name="Version">The version string of the created configuration.</param>
/// <param name="TenantId">The tenant that owns the configuration.</param>
public sealed record AgentConfigurationCreated(
    Guid ConfigurationId,
    string Name,
    string Version,
    Guid TenantId) : IDomainEvent
{
    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
