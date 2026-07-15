namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Provides a resilience pipeline that wraps operations with retry and circuit-breaker policies.
/// </summary>
public interface IResiliencePipelineProvider
{
    /// <summary>
    /// Executes the specified operation with retry and circuit-breaker policies applied.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The operation to execute, receiving a cancellation token.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task whose result contains the value produced by the operation.</returns>
    Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}
