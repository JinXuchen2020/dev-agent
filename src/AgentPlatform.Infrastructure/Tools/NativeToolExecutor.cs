using System.Net;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Tools;

/// <summary>
/// 执行平台内置的原生工具：对 <see cref="ToolDefinition.EndpointUrl"/> 发起真实 HTTP 调用，
/// 回传真实响应体与状态码；非 2xx / 超时 / 连接失败 → 精准回打真实错误（符合 Phase 6 critic 范式）。
/// </summary>
internal sealed class NativeToolExecutor : IToolExecutor
{
    private readonly ILogger<NativeToolExecutor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SandboxSettings _settings;

    public NativeToolExecutor(
        ILogger<NativeToolExecutor> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<SandboxSettings> settings)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    /// <summary>Gets the tool source handled by this executor, which is <see cref="ToolSource.NativeTool"/>.</summary>
    public ToolSource Source => ToolSource.NativeTool;

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool, string parametersJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tool.EndpointUrl))
        {
            _logger.LogWarning("原生工具 '{ToolName}' 未配置 EndpointUrl，无法真实执行", tool.Name);
            return new ToolExecutionResult(false, string.Empty, null,
                "原生工具未配置 EndpointUrl，无法真实执行");
        }

        var method = ResolveMethod(parametersJson);
        var client = _httpClientFactory.CreateClient(nameof(NativeToolExecutor));
        client.Timeout = TimeSpan.FromSeconds(_settings.HttpTimeoutSeconds);

        using var request = new HttpRequestMessage(method, tool.EndpointUrl);
        if (method != HttpMethod.Get && !string.IsNullOrWhiteSpace(parametersJson))
        {
            request.Content = new StringContent(parametersJson, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("原生工具 '{ToolName}' 真实调用 {Method} {Url}", tool.Name, method, tool.EndpointUrl);

        try
        {
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            body = Truncate(body, _settings.MaxOutputBytes);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("原生工具 '{ToolName}' 调用成功 ({(int)response.StatusCode})", tool.Name, response.StatusCode);
                return new ToolExecutionResult(true, body);
            }

            var err = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            _logger.LogWarning("原生工具 '{ToolName}' 调用失败：{Error}", tool.Name, err);
            return new ToolExecutionResult(false, body, null, err);
        }
        catch (OperationCanceledException)
        {
            return new ToolExecutionResult(false, string.Empty, null, "工具调用超时");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "原生工具 '{ToolName}' 调用异常", tool.Name);
            return new ToolExecutionResult(false, string.Empty, null, ex.Message);
        }
    }

    private static HttpMethod ResolveMethod(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson)) return HttpMethod.Get;
        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            if (doc.RootElement.TryGetProperty("httpMethod", out var m) &&
                m.ValueKind == JsonValueKind.String &&
                Enum.TryParse<HttpMethodEnum>(m.GetString(), ignoreCase: true, out var parsed))
            {
                return parsed switch
                {
                    HttpMethodEnum.Get => HttpMethod.Get,
                    HttpMethodEnum.Post => HttpMethod.Post,
                    HttpMethodEnum.Put => HttpMethod.Put,
                    HttpMethodEnum.Delete => HttpMethod.Delete,
                    _ => HttpMethod.Post
                };
            }
        }
        catch (JsonException) { /* 忽略，走默认 POST */ }
        return HttpMethod.Post;
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxBytes) return value;
        return value.Substring(0, maxBytes);
    }

    private enum HttpMethodEnum { Get, Post, Put, Delete }
}
