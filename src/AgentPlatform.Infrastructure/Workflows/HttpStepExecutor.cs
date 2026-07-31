using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// HTTP 节点执行器（<see cref="StepType.Http"/>）。
/// 用 <see cref="IHttpClientFactory"/> 向外部服务发起真实 HTTP 请求，响应体作为下游 artifact。
/// 配置（<c>ConfigJson</c>）：<c>method</c>、<c>url</c>、<c>headers</c>（对象）、<c>bodyTemplate</c>（可选，支持 {{name}} 占位替换为 artifact/blackboard）、<c>authRef</c>（预留）。
/// 出站请求受 30s 超时约束，防止恶意长阻塞。
/// </summary>
internal sealed class HttpStepExecutor : IStepExecutor
{
    private const int MaxTimeoutSeconds = 30;
    private static readonly Regex s_placeholder = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    private readonly ILogger<HttpStepExecutor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpStepExecutor(ILogger<HttpStepExecutor> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public string StepType => "*";
    public StepType? HandlesType => AgentPlatform.Domain.Enums.StepType.Http;

    public async Task<StepExecutionResult> ExecuteAsync(IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(ctx);

        try
        {
            var config = ParseConfig(step.ConfigJson);
            if (string.IsNullOrWhiteSpace(config.Url))
                return StepExecutionResult.FatalFailure("HTTP 节点未配置 url");

            var url = Substitute(config.Url, ctx);
            var method = string.IsNullOrWhiteSpace(config.Method) ? HttpMethod.Get : new HttpMethod(config.Method);
            using var request = new HttpRequestMessage(method, url);

            if (config.Headers.Count > 0)
            {
                foreach (var (k, v) in config.Headers)
                    request.Headers.TryAddWithoutValidation(k, Substitute(v, ctx));
            }

            if (!string.IsNullOrWhiteSpace(config.BodyTemplate) &&
                method != HttpMethod.Get && method != HttpMethod.Head)
            {
                request.Content = new StringContent(Substitute(config.BodyTemplate, ctx), System.Text.Encoding.UTF8);
            }

            var client = _httpClientFactory.CreateClient("workflow-http");
            using var timeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(MaxTimeoutSeconds));

            _logger.LogInformation("HTTP 节点 {StepName}：{Method} {Url}", step.Name, method, url);
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            var artifact = JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                body = Truncate(body, 4000)
            });

            if (response.IsSuccessStatusCode)
                return StepExecutionResult.Success(body, artifact);

            _logger.LogWarning("HTTP 节点 {StepName}：状态码 {Code}", step.Name, (int)response.StatusCode);
            return StepExecutionResult.RetryableFailure($"HTTP 状态码 {(int)response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return StepExecutionResult.RetryableFailure("HTTP 节点超时");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP 节点 {StepName} 失败：{Message}", step.Name, ex.Message);
            return StepExecutionResult.RetryableFailure(ex.Message);
        }
    }

    private static string Substitute(string template, WorkflowContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, art) in ctx.Artifacts)
            map[name] = art.Content;
        foreach (var (k, v) in ctx.Blackboard.Entries)
            map[k] = v;

        return s_placeholder.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return map.TryGetValue(key, out var val) ? val : m.Value;
        });
    }

    private HttpNodeConfig ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new HttpNodeConfig(null, null, new Dictionary<string, string>(), null, null);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            string? url = root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            string? body = root.TryGetProperty("bodyTemplate", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            string? authRef = root.TryGetProperty("authRef", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;

            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in h.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        headers[prop.Name] = prop.Value.GetString()!;
            }

            return new HttpNodeConfig(method, url, headers, body, authRef);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "HTTP 节点配置 JSON 解析失败");
            return new HttpNodeConfig(null, null, new Dictionary<string, string>(), null, null);
        }
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value.Substring(0, max);

    private sealed record HttpNodeConfig(
        string? Method, string? Url, Dictionary<string, string> Headers, string? BodyTemplate, string? AuthRef);
}
