namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides operations for storing and retrieving short-lived values in a transient key-value store,
/// typically used for conversation-scoped or session-scoped memory.
/// </summary>
public interface IShortTermMemory
{
    /// <summary>
    /// Stores a value in short-term memory under the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the value to store.</typeparam>
    /// <param name="key">The cache key under which the value is stored.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="expiry">An optional time-to-live after which the entry expires.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous set operation.</returns>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the value associated with the specified key, if present.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve.</typeparam>
    /// <param name="key">The cache key to look up.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result is the stored value, or <c>default</c> if the key was not found.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes the value associated with the specified key from short-term memory.
    /// </summary>
    /// <param name="key">The cache key to remove.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous remove operation.</returns>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
