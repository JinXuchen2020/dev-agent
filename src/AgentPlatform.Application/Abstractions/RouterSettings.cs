namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Contains configuration for the model router, including per-tenant daily budget and resilience policies.
/// Platform candidate models are sourced from the DB-backed <c>PlatformModels</c> catalog (not configuration).
/// </summary>
public sealed class RouterSettings
{
    /// <summary>
    /// Gets or sets the maximum amount a single tenant may spend per day on platform-provided models.
    /// BYO-key (tenant-owned) models are not subject to this budget (cost is borne by the tenant).
    /// Default 1.00 USD/tenant/day (F13 S2).
    /// </summary>
    public decimal PerTenantDailyBudget { get; set; } = 1.0m;

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
    /// Agentic 多轮任务里每一轮模型调用都可能携带很长的工具历史；设 0（默认）表示
    /// 禁用单次调用超时，让长生成可以一直跑到完成（配合前端"无限运行直到目标达成"）。
    /// 若担心半开流挂死，可配置一个正数（如 300）作为单次调用的兜底上限。
    /// </summary>
    public double TimeoutSeconds { get; set; } = 0;
}
