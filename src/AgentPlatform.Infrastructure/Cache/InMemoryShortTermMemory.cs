using System.Collections.Concurrent;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Cache;

/// <summary>
/// In-memory implementation of <see cref="IShortTermMemory"/> that stores cached values in a concurrent dictionary with optional expiry.
/// </summary>
internal sealed class InMemoryShortTermMemory : IShortTermMemory
{
    private readonly ILogger<InMemoryShortTermMemory> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new();

    private record CacheEntry(object Value, DateTime? ExpiresAt);

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryShortTermMemory"/> class.
    /// </summary>
    /// <param name="logger">The logger used to capture cache operation diagnostics.</param>
    public InMemoryShortTermMemory(ILogger<InMemoryShortTermMemory> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Stores a value in the cache under the specified key with an optional expiry.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key under which to store the value.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="expiry">The optional time-to-live after which the entry expires.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous set operation.</returns>
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var expiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
        _memory[key] = new CacheEntry(value!, expiresAt);
        _logger.LogDebug("Cached key: {Key} (expires: {Expiry})", key, expiry);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves the value associated with the specified key, returning <c>default</c> if the key is missing or expired.
    /// </summary>
    /// <typeparam name="T">The type of the value to retrieve.</typeparam>
    /// <param name="key">The cache key whose value to retrieve.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the cached value, or <c>default</c> if not found or expired.</returns>
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_memory.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
            {
                _memory.TryRemove(key, out _);
                return Task.FromResult<T?>(default);
            }

            if (entry.Value is T typed)
                return Task.FromResult<T?>(typed);
        }

        return Task.FromResult<T?>(default);
    }

    /// <summary>
    /// Removes the value associated with the specified key from the cache.
    /// </summary>
    /// <param name="key">The cache key whose value to remove.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous remove operation.</returns>
    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _memory.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
