using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.AgentConfigurations.Events;

/// <summary>
/// Domain event raised when an existing agent configuration is updated.
/// </summary>
/// <param name="ConfigurationId">The unique identifier of the configuration.</param>
/// <param name="NewVersion">The version string after the update.</param>
/// <param name="PreviousVersion">The version string before the update.</param>
/// <param name="TenantId">The tenant that owns the configuration.</param>
public sealed record AgentConfigurationUpdated(
    Guid ConfigurationId,
    string NewVersion,
    string PreviousVersion,
    Guid TenantId) : IDomainEvent
{
    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
