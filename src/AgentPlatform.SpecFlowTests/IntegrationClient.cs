using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// BDD HTTP 客户端辅助：经真实管线 <see cref="IntegrationHost.Api"/> 发请求，统一 camelCase 序列化
/// 与 Bearer 注入。所有认证经真实登录端点拿 JWT（不伪造 token），满足设计文档 §4.2。
/// </summary>
public static class IntegrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>以 T1 种子 admin 登录，返回 JWT。每次调用重新登录，避免令牌过期导致偶发 401。</summary>
    public static async Task<string> AdminTokenAsync()
        => await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);

    /// <summary>发送已认证/未认证请求。body 自动序列化为 camelCase JSON。</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string? bearer = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (bearer is not null)
            req.WithBearer(bearer);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return await IntegrationHost.Api.SendAsync(req);
    }

    /// <summary>将响应体反序列化为 T（camelCase + 大小写不敏感）。</summary>
    public static async Task<T?> ReadAsAsync<T>(HttpResponseMessage response)
        => JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), JsonOptions);
}
