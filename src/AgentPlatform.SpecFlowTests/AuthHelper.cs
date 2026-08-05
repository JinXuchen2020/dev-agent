using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgentPlatform.SpecFlowTests;

/// <summary>
/// 认证辅助：真实走登录端点拿 API 签发的 JWT（httpOnly cookie 中的 ap_access_token），
/// 供 BDD 步骤构造已认证 HttpClient。与 Api.Tests 不同，这里不手工伪造 JWT，
/// 而是验证真实登录链路（设计文档 §4.2）。
/// </summary>
public static class AuthHelper
{
    /// <summary>
    /// 用邮箱 + 密码登录，从 Set-Cookie 中提取 ap_access_token 并返回。
    /// </summary>
    /// <param name="tenantId">可选租户覆盖：匿名登录（TenantProvider 优先级 2 的 X-Tenant-Id 头）。
    /// 用于跨租户种子用户登录——默认 DefaultTenantId 只能命中 T1，T2 用户必须显式带此头，否则按 T1 解析找不到用户。</param>
    /// <remarks>
    /// 每次登录使用工厂新建的独立 HttpClient（独立 Cookie 容器），避免共享 IntegrationHost.Api
    /// 累积的既有 ap_access_token cookie 泄漏进来：该 cookie 携带 JWT 的 tenant_id=T1 声明，
    /// 会被 TenantProvider 优先于 X-Tenant-Id 头解析，导致跨租户登录误落到默认租户而 user_not_found
    /// （设计文档 §4.2 风险：共享 client 的 cookie 跨场景污染）。登录拿到的 token 由调用方经
    /// WithBearer 显式附加，不依赖 client 自身 cookie。
    /// </remarks>
    public static async Task<string> LoginAsync(string email, string password, Guid? tenantId = null)
    {
        // 独立 client：不继承任何先前场景留下的认证 cookie。
        using var client = IntegrationHost.Factory.CreateClient();

        var body = JsonSerializer.Serialize(new { email, password });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = content };
        if (tenantId is not null)
            request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());
        using var response = await client.SendAsync(request);

        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            throw new InvalidOperationException($"登录未返回 Set-Cookie（状态码 {response.StatusCode}）。");

        foreach (var header in values)
        {
            // 格式：ap_access_token=<jwt>; path=/; httponly; samesite=lax
            var name = header.Split(';', 2)[0];
            var pair = name.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim() == "ap_access_token")
                return pair[1].Trim();
        }

        throw new InvalidOperationException("Set-Cookie 中未找到 ap_access_token。");
    }

    /// <summary>构造带 Bearer 令牌的请求消息（Smart 策略 → Bearer 方案）。</summary>
    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>在请求上附加 ApiKey 头（Smart 策略 → ApiKey 方案）。</summary>
    public static HttpRequestMessage WithApiKey(this HttpRequestMessage request, string apiKey)
    {
        request.Headers.Add("X-API-Key", apiKey);
        return request;
    }

    /// <summary>读取响应体字符串（扩展方法）。</summary>
    public static async Task<string> ReadBodyAsync(this HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync();
}
