using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// F12 BDD HTTP 客户端辅助：经真实管线 <see cref="F12IntegrationHost.Api"/> 发请求（保留真实
/// Code/Tool 执行器的工厂），统一 camelCase 序列化与 Bearer 注入。与基 <see cref="IntegrationClient"/>
/// 对称，但指向 F12 宿主。
/// </summary>
public static class F12IntegrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>发送已认证/未认证请求。body 自动序列化为 camelCase JSON。</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? bearer = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (bearer is not null)
            req.WithBearer(bearer);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await F12IntegrationHost.Api.SendAsync(req);
    }

    /// <summary>将响应体反序列化为 T（camelCase + 大小写不敏感）。</summary>
    public static async Task<T?> ReadAsAsync<T>(HttpResponseMessage response)
        => JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), JsonOptions);
}
