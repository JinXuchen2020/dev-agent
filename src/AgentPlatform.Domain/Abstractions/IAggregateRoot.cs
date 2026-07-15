namespace AgentPlatform.Domain.Abstractions;

/// <summary>
/// Defines the contract for an aggregate root, which serves as the consistency
/// boundary for a cluster of domain objects and manages its domain events.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>
    /// Gets the collection of domain events that have been raised by this aggregate
    /// and are awaiting dispatch.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Clears all pending domain events from this aggregate after they have been dispatched.
    /// </summary>
    void ClearDomainEvents();
}
