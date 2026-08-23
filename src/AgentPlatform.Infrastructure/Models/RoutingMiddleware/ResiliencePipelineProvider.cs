using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace AgentPlatform.Infrastructure.Models.RoutingMiddleware;

/// <summary>
/// Provides a Polly resilience pipeline with retry and circuit-breaker policies for routing model invocations.
/// </summary>
internal sealed class ResiliencePipelineProvider : IResiliencePipelineProvider
{
    private readonly ResiliencePipeline _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResiliencePipelineProvider"/> class, building retry and circuit-breaker strategies from the configured router settings.
    /// </summary>
    /// <param name="routerOptions">The configured router settings controlling retry and circuit-breaker behaviour.</param>
    public ResiliencePipelineProvider(IOptions<RouterSettings> routerOptions)
    {
        var settings = routerOptions.Value;

        // TimeoutSeconds <= 0（默认）表示禁用单次调用超时，不加入 Polly 超时策略；
        // 设正数时才作为单次模型调用的兜底上限（防半开流挂死）。
        var builder = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = settings.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(settings.RetryDelayMilliseconds),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = settings.CircuitBreakerFailureRatio,
                MinimumThroughput = settings.CircuitBreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            });

        if (settings.TimeoutSeconds > 0)
        {
            builder = builder.AddTimeout(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        }

        _pipeline = builder.Build();
    }

    /// <summary>
    /// Executes the supplied operation through the resilience pipeline, applying retry and circuit-breaker policies.
    /// </summary>
    /// <typeparam name="T">The type of the value returned by the operation.</typeparam>
    /// <param name="operation">The asynchronous operation to execute with resilience.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>A task that completes with the result of the operation once it succeeds or the pipeline exhausts retries.</returns>
    public async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        T result = default!;
        await _pipeline.ExecuteAsync(async pipelineCt =>
        {
            result = await operation(pipelineCt);
        }, ct);
        return result;
    }
}
