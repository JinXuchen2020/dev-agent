using System.Net.Http.Headers;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Models;

/// <summary>
/// 真实向 provider 的 OpenAI 兼容 GET /models 端点发起请求并解析模型清单。
/// 复用 <c>SerpApiSearchProvider</c> 的出站模式（IHttpClientFactory + 请求级超时 + 错误透传，非伪造）。
/// 密钥仅一次性探测：不落库、不写日志。失败以 <see cref="ProviderModelDiscoveryException"/> 携带中文原因。
/// </summary>
internal sealed class ProviderModelDiscovery : IProviderModelDiscovery
{
    private static readonly IReadOnlyDictionary<string, string> DefaultBaseUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenAI"] = "https://api.openai.com/v1",
            ["DeepSeek"] = "https://api.deepseek.com/v1",
        };

    private static readonly HashSet<string> KnownProviders =
        new(StringComparer.OrdinalIgnoreCase) { "OpenAI", "DeepSeek", "VLLM", "Custom" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderModelDiscovery> _logger;

    public ProviderModelDiscovery(IHttpClientFactory httpClientFactory, ILogger<ProviderModelDiscovery> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProviderModelInfo>> DiscoverAsync(
        string provider, string apiKey, string? baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || !KnownProviders.Contains(provider))
            throw new ProviderModelDiscoveryException(
                $"不支持的 Provider：{provider ?? "（空）"}（仅支持 OpenAI / DeepSeek / VLLM / Custom）");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ProviderModelDiscoveryException("API Key 不能为空");

        var resolvedBase = ResolveBaseUrl(provider, baseUrl);
        var modelsUrl = $"{resolvedBase.TrimEnd('/')}/models";

        _logger.LogInformation("探测 provider={Provider} 模型清单 base={Base}", provider, resolvedBase);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        var client = _httpClientFactory.CreateClient(nameof(ProviderModelDiscovery));
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 整段（请求 + 读取响应体 + 解析）都受请求级超时保护：超时既可能发生在连接阶段，
        // 也可能发生在读取响应体阶段，任一阶段超时都应映射为友好 400 而非 500。
        string json;
        try
        {
            using var response = await client.SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var reason = response.ReasonPhrase ?? "";
                var message = status switch
                {
                    401 or 403 => "API Key 无效或无权访问该 provider 的模型列表",
                    404 => "该端点不支持 /models，请检查 Base URL 是否正确",
                    _ => $"Provider 返回 {status} {reason}"
                };
                throw new ProviderModelDiscoveryException(message);
            }

            json = await response.Content.ReadAsStringAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            throw new ProviderModelDiscoveryException("模型清单请求超时（>15s）");
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderModelDiscoveryException($"模型清单请求失败：{ex.Message}");
        }

        return ParseModels(json);
    }

    private static string ResolveBaseUrl(string provider, string? baseUrl)
    {
        if (DefaultBaseUrls.TryGetValue(provider, out var def))
        {
            return string.IsNullOrWhiteSpace(baseUrl) ? def : baseUrl!;
        }

        // VLLM / Custom：必须用户提供 BaseUrl（无默认端点）。
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ProviderModelDiscoveryException($"{provider} 必须填写 Base URL");
        return baseUrl!;
    }

    private static IReadOnlyList<ProviderModelInfo> ParseModels(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ProviderModelInfo>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            // 非标准响应：容忍缺 data 数组，视为无模型（避免 500）。
            return Array.Empty<ProviderModelInfo>();
        }

        var list = new List<ProviderModelInfo>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            var id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            string? ownedBy = null;
            if (item.TryGetProperty("owned_by", out var ownedEl) && ownedEl.ValueKind == JsonValueKind.String)
                ownedBy = ownedEl.GetString();
            list.Add(new ProviderModelInfo(id!, ownedBy));
        }

        return list;
    }
}
