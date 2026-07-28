namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// 供应商账户下可访问的模型标识（OpenAI 兼容 GET /models 响应解析结果）。不含任何密钥。
/// </summary>
public sealed record ProviderModelInfo(string Id, string? OwnedBy = null);

/// <summary>
/// 探测供应商模型清单时抛出的领域友好异常。携带可直接回传给客户端的 400 中文原因，绝不泄露密钥。
/// </summary>
public sealed class ProviderModelDiscoveryException : Exception
{
    /// <summary>使用可直接回传给客户端的中文原因初始化异常。</summary>
    /// <param name="message">面向用户的中文错误原因（不含密钥）。</param>
    public ProviderModelDiscoveryException(string message) : base(message) { }
}

/// <summary>
/// 探测供应商账户下所有可访问模型（OpenAI 兼容 GET /v1/models）。
/// 仅用于一次性探测：密钥不落库、不写日志；失败时以 <see cref="ProviderModelDiscoveryException"/> 携带中文原因。
/// Provider 范围对齐 F13（OpenAI / DeepSeek / VLLM / Custom，均 OpenAI 兼容）。
/// </summary>
public interface IProviderModelDiscovery
{
    /// <summary>探测模型清单。</summary>
    /// <param name="provider">OpenAI / DeepSeek / VLLM / Custom（均 OpenAI 兼容）。</param>
    /// <param name="apiKey">探测用密钥，仅本次出站使用。</param>
    /// <param name="baseUrl">OpenAI 兼容端点；OpenAI/DeepSeek 可留空自动补默认，VLLM/Custom 必填。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<ProviderModelInfo>> DiscoverAsync(
        string provider, string apiKey, string? baseUrl, CancellationToken ct = default);
}
