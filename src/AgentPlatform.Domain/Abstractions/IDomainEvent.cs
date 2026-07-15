namespace AgentPlatform.Domain.Abstractions;

/// <summary>
/// Defines the contract for a domain event that records a significant occurrence
/// within the domain, carrying the timestamp of when it occurred.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the UTC timestamp when the domain event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}
