namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration settings for Redis cache connectivity, including connection string,
/// default expiry policy, and key namespace prefix.
/// </summary>
public sealed class RedisSettings
{
    /// <summary>
    /// Gets or sets the Redis connection string. Defaults to the local Redis instance.
    /// Default: "localhost:6379"
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the default Time-To-Live in seconds for cached entries.
    /// Default: 3600 (1 hour)
    /// </summary>
    public int DefaultExpirySeconds { get; set; } = 3600;

    /// <summary>
    /// Gets or sets an optional key prefix used to namespace Redis keys for this application.
    /// Default: "agent-platform:"
    /// </summary>
    public string KeyPrefix { get; set; } = "agent-platform:";
}
