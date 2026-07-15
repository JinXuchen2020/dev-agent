using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Agents.Events;

/// <summary>
/// Represents a domain event raised when a new agent is created within the platform.
/// </summary>
/// <param name="AgentId">The unique identifier of the newly created agent.</param>
/// <param name="Name">The display name of the agent.</param>
/// <param name="RoleCode">The string representation of the agent's role.</param>
/// <param name="TenantId">The identifier of the tenant that owns the agent.</param>
public record AgentCreated(
    Guid AgentId,
    string Name,
    string RoleCode,
    Guid TenantId
) : IDomainEvent
{
    /// <summary>
    /// Gets the UTC timestamp when the agent creation event occurred.
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
