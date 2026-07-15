using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Domain.Services;

// 阶段二实现: 使用 PricingSettings 配置驱动, 不再硬编码定价表
/// <summary>
/// Provides domain logic for evaluating model endpoint fallback policies,
/// determining whether a fallback endpoint is viable when the primary endpoint fails.
/// </summary>
public static class RoutingPolicyDomainService
{
    /// <summary>
    /// 最小可用上下文窗口大小（token 数），低于此值的模型不视为可用回退端点。
    /// </summary>
    public const int MinViableContextWindow = 1024;

    /// <summary>
    /// Determines whether the specified fallback <see cref="ModelEndpoint"/> is a viable
    /// fallback for the primary endpoint, based on having a valid API URL and a sufficient
    /// context window.
    /// </summary>
    /// <param name="primary">The primary model endpoint that may have failed.</param>
    /// <param name="fallback">The candidate fallback model endpoint to evaluate.</param>
    /// <returns><c>true</c> if the fallback endpoint has a non-empty API URL and at least 1024 maximum tokens; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="primary"/> is <c>null</c>.</exception>
    public static bool CanFallback(ModelEndpoint primary, ModelEndpoint fallback)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        return !string.IsNullOrWhiteSpace(fallback.ApiUrl)
            && fallback.MaxTokens >= MinViableContextWindow;
    }
}
