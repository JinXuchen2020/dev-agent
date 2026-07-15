using System.Collections.Concurrent;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AgentPlatform.Infrastructure.Cache;

/// <summary>
/// Redis-backed implementation of <see cref="IShortTermMemory"/> that stores values as JSON strings
/// in a Redis instance. Falls back to an in-memory dictionary when Redis is unreachable.
/// </summary>
internal sealed class RedisShortTermMemory : IShortTermMemory
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisShortTermMemory> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _fallbackMemory = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private bool _redisFailed;

    private sealed record CacheEntry(object Value, DateTime? ExpiresAt);
    private readonly object _fallbackLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisShortTermMemory"/> class.
    /// </summary>
    /// <param name="connection">The Redis connection multiplexer.</param>
    /// <param name="settings">The Redis configuration settings.</param>
    /// <param name="logger">The logger used to capture cache operation diagnostics.</param>
    public RedisShortTermMemory(
        IConnectionMultiplexer connection,
        IOptions<RedisSettings> settings,
        ILogger<RedisShortTermMemory> logger)
    {
        _connection = connection;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Stores a value in Redis under the specified key with an optional expiry.
    /// Falls back to in-memory storage if Redis is unreachable.
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixedKey = GetPrefixedKey(key);

        try
        {
            var db = _connection.GetDatabase();
            var json = JsonSerializer.Serialize(value, _jsonOptions);

            if (expiry.HasValue)
            {
                await db.StringSetAsync(prefixedKey, json, expiry.Value);
            }
            else
                if (_settings.DefaultExpirySeconds > 0)
            {
                var defaultExpiry = TimeSpan.FromSeconds(_settings.DefaultExpirySeconds);
                await db.StringSetAsync(prefixedKey, json, defaultExpiry);
            }
            else
            {
                await db.StringSetAsync(prefixedKey, json);
            }

            if (_redisFailed)
            {
                _redisFailed = false;
                _logger.LogInformation("Redis connection restored; cleared fallback flag");
            }
        }
        catch (RedisConnectionException ex)
        {
            _redisFailed = true;
            _logger.LogWarning(ex, "Redis unreachable; falling back to in-memory cache for key: {Key}", key);
            StoreFallback(prefixedKey, value, expiry);
        }
        catch (RedisServerException ex)
        {
            _redisFailed = true;
            _logger.LogWarning(ex, "Redis error; falling back to in-memory cache for key: {Key}", key);
            StoreFallback(prefixedKey, value, expiry);
        }
        catch (TimeoutException ex)
        {
            _redisFailed = true;
            _logger.LogWarning(ex, "Redis timeout; falling back to in-memory cache for key: {Key}", key);
            StoreFallback(prefixedKey, value, expiry);
        }
    }

    /// <summary>
    /// Retrieves a value from Redis by key, returning <c>default</c> if the key is missing or expired.
    /// Falls back to in-memory storage if Redis is unreachable.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixedKey = GetPrefixedKey(key);

        try
        {
            var db = _connection.GetDatabase();
            var redisValue = await db.StringGetAsync(prefixedKey);

            if (redisValue.HasValue)
            {
                return JsonSerializer.Deserialize<T>(redisValue!, _jsonOptions);
            }

            return default;
        }
        catch (RedisConnectionException ex)
        {
            _redisFailed = true;
            _logger.LogDebug(ex, "Redis unreachable during get for key: {Key}; checking fallback", key);
            return GetFallback<T>(prefixedKey);
        }
        catch (RedisServerException ex)
        {
            _redisFailed = true;
            _logger.LogDebug(ex, "Redis error during get for key: {Key}; checking fallback", key);
            return GetFallback<T>(prefixedKey);
        }
        catch (TimeoutException ex)
        {
            _redisFailed = true;
            _logger.LogDebug(ex, "Redis timeout during get for key: {Key}; checking fallback", key);
            return GetFallback<T>(prefixedKey);
        }
    }

    /// <summary>
    /// Removes a value from Redis by key.
    /// Falls back to in-memory storage if Redis is unreachable.
    /// </summary>
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var prefixedKey = GetPrefixedKey(key);

        try
        {
            var db = _connection.GetDatabase();
            await db.KeyDeleteAsync(prefixedKey);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogDebug(ex, "Redis unreachable during remove for key: {Key}; removing from fallback", key);
        }
        catch (RedisServerException ex)
        {
            _logger.LogDebug(ex, "Redis error during remove for key: {Key}; removing from fallback", key);
        }
        catch (TimeoutException ex)
        {
            _logger.LogDebug(ex, "Redis timeout during remove for key: {Key}; removing from fallback", key);
        }

        // Always clean up fallback memory regardless of Redis outcome
        _fallbackMemory.TryRemove(prefixedKey, out _);
    }

    private string GetPrefixedKey(string key) => $"{_settings.KeyPrefix}{key}";

    private void StoreFallback<T>(string prefixedKey, T value, TimeSpan? expiry)
    {
        lock (_fallbackLock)
        {
            var expiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
            _fallbackMemory[prefixedKey] = new CacheEntry(value!, expiresAt);
        }
    }

    private T? GetFallback<T>(string prefixedKey)
    {
        lock (_fallbackLock)
        {
            if (_fallbackMemory.TryGetValue(prefixedKey, out var entry))
            {
                if (entry.ExpiresAt.HasValue && DateTime.UtcNow > entry.ExpiresAt.Value)
                {
                    _fallbackMemory.TryRemove(prefixedKey, out _);
                    return default;
                }

                if (entry.Value is T typed)
                    return typed;
            }

            return default;
        }
    }
}
