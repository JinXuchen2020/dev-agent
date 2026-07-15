using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for persisting aggregate changes and tracking aggregates within a unit of work.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes tracked by this unit of work.
    /// </summary>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result is the number of state entries written to the data store.</returns>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the collection of aggregate roots currently tracked by this unit of work.
    /// </summary>
    /// <returns>A read-only collection of tracked aggregate roots.</returns>
    IReadOnlyCollection<IAggregateRoot> GetTrackedAggregates();
}
