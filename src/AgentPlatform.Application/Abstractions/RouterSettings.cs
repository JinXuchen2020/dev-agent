namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains configuration for the model router, including candidate models, daily budget, and resilience policies.
/// </summary>
public sealed class RouterSettings
{
    /// <summary>
    /// Gets or sets the list of model candidates available for routing.
    /// </summary>
    public List<ModelCandidateConfig> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum amount that may be spent per day across all model calls.
    /// </summary>
    public decimal DailyBudget { get; set; } = 50.0m;

    /// <summary>
    /// Gets or sets the default estimated number of tokens used for cost reservation when actual usage is unknown.
    /// </summary>
    public int DefaultEstimatedTokens { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failed model call.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay in milliseconds between retry attempts.
    /// </summary>
    public double RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Gets or sets the failure ratio threshold above which the circuit breaker opens.
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the minimum number of calls that must pass through before the circuit breaker can trip.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>
    /// Gets or sets the duration in seconds for which the circuit breaker stays open before resetting.
    /// </summary>
    public double CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the overall timeout in seconds for a single model invocation.
    /// </summary>
    public double TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Represents the configuration for a single model candidate used by the router.
/// </summary>
public sealed class ModelCandidateConfig
{
    /// <summary>
    /// Gets or sets the unique identifier of the model (e.g., "gpt-4o").
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider hosting the model (e.g., "openai", "anthropic").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the priority of this candidate; higher values indicate stronger preference during routing.
    /// </summary>
    public int Priority { get; set; }
}
