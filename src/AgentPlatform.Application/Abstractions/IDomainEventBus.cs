using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides a mechanism for publishing domain events to interested handlers.
/// </summary>
public interface IDomainEventBus
{
    /// <summary>
    /// Publishes the specified domain event to all registered handlers.
    /// </summary>
    /// <typeparam name="T">The type of the domain event.</typeparam>
    /// <param name="domainEvent">The domain event instance to publish.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    Task PublishAsync<T>(T domainEvent, CancellationToken ct = default) where T : IDomainEvent;
}
