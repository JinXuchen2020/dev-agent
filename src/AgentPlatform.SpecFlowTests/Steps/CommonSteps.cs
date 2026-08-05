using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 跨 feature 共享的 BDD 步骤基件：登录（admin / T2 / 成员）、以指定身份发送 HTTP 请求
/// （支持 Gherkin DocString 携带 JSON 正文，可选）、响应状态码 / 响应体 / JSON 属性断言。
/// 所有请求经真实管线 <see cref="IntegrationHost.Api"/>，连真实文件 SQLite（设计文档 §4.2）。
///
/// 绑定字符串均以 ^...$ 锚定（与 PublishedWorkflowSteps 约定一致），角色分支用 (admin|T2 用户|成员)。
/// 每个步骤同时登记 Given/When/Then 三种关键字，避免 Reqnroll 在 <c>And</c>/<c>But</c> 推导后
/// （如 And 跟随 Given 会被推导为 Given）因关键字不匹配而报“无匹配步骤”。
/// 发送类步骤提供“无正文”与“带 DocString 正文”两种参数个数，由 Reqnroll 按是否存在多行文本自动选择。
/// </summary>
[Binding]
public sealed class CommonSteps
{
    private readonly ScenarioContext _scenario;

    // 令牌按角色缓存（有效期 1h，单次测试运行内不会过期），避免每步重复登录。
    private static readonly ConcurrentDictionary<string, string> TokenCache = new();

    public CommonSteps(ScenarioContext scenario)
    {
        _scenario = scenario;
    }

    // ── 登录（存令牌到缓存，供后续步骤取用）─────────────────────────
    [Given("^以集成租户 T1 admin 身份已登录$")]
    [When("^以集成租户 T1 admin 身份已登录$")]
    [Then("^以集成租户 T1 admin 身份已登录$")]
    public void GivenAdminLoggedIn() => CacheToken("admin",
        () => AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword));

    [Given("^以租户 T2 用户身份已登录$")]
    [When("^以租户 T2 用户身份已登录$")]
    [Then("^以租户 T2 用户身份已登录$")]
    public void GivenT2LoggedIn() => CacheToken("t2",
        () => AuthHelper.LoginAsync(IntegrationConstants.Tenant2UserEmail, IntegrationConstants.Tenant2UserPassword, IntegrationConstants.Tenant2Id));

    [Given("^以 T1 非 Admin 成员身份已登录$")]
    [When("^以 T1 非 Admin 成员身份已登录$")]
    [Then("^以 T1 非 Admin 成员身份已登录$")]
    public void GivenMemberLoggedIn() => CacheToken("member",
        () => AuthHelper.LoginAsync(IntegrationConstants.NonAdminEmail, IntegrationConstants.NonAdminPassword));

    // ── 以指定身份发送请求（无正文）────────────────────────────────
    [Given("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [When("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [Then("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    public async Task SendAsRole(string role, string method, string url)
        => await SendAsync(RoleToken(role), method, url, null);

    // ── 以指定身份发送请求（带 DocString JSON 正文）─────────────────
    [Given("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [When("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [Then("^以 (admin|T2 用户|成员) 身份发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    public async Task SendAsRoleWithBody(string role, string method, string url, string docString)
        => await SendAsync(RoleToken(role), method, url, docString);

    // ── 匿名发送请求（无正文）──────────────────────────────────────
    [Given("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [When("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [Then("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    public async Task SendAnonymous(string method, string url)
        => await SendAsync(null, method, url, null);

    // ── 匿名发送请求（带 DocString JSON 正文）──────────────────────
    [Given("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [When("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    [Then("^匿名发送 (GET|POST|PUT|DELETE) 请求到 \"([^\"]*)\"$")]
    public async Task SendAnonymousWithBody(string method, string url, string docString)
        => await SendAsync(null, method, url, docString);

    // ── 响应断言 ────────────────────────────────────────────────────
    [Given("^响应状态码为 (\\d{3})$")]
    [When("^响应状态码为 (\\d{3})$")]
    [Then("^响应状态码为 (\\d{3})$")]
    public void ThenStatusCode(int expected)
        => Assert.Equal((HttpStatusCode)expected, LastResponse.StatusCode);

    [Given("^响应体包含 \"([^\"]*)\"$")]
    [When("^响应体包含 \"([^\"]*)\"$")]
    [Then("^响应体包含 \"([^\"]*)\"$")]
    public void ThenBodyContains(string fragment)
        => Assert.Contains(fragment, LastBody ?? string.Empty);

    [Given("^响应体不包含 \"([^\"]*)\"$")]
    [When("^响应体不包含 \"([^\"]*)\"$")]
    [Then("^响应体不包含 \"([^\"]*)\"$")]
    public void ThenBodyNotContains(string fragment)
        => Assert.DoesNotContain(fragment, LastBody ?? string.Empty);

    [Given("^响应 JSON 含属性 \"([^\"]*)\"$")]
    [When("^响应 JSON 含属性 \"([^\"]*)\"$")]
    [Then("^响应 JSON 含属性 \"([^\"]*)\"$")]
    public void ThenJsonHasProperty(string property)
    {
        using var doc = JsonDocument.Parse(LastBody ?? "{}");
        Assert.True(TryGetProperty(doc.RootElement, property, out _), $"响应 JSON 缺少属性 {property}");
    }

    [Given("^响应 JSON 属性 \"([^\"]*)\" 等于 \"([^\"]*)\"$")]
    [When("^响应 JSON 属性 \"([^\"]*)\" 等于 \"([^\"]*)\"$")]
    [Then("^响应 JSON 属性 \"([^\"]*)\" 等于 \"([^\"]*)\"$")]
    public void ThenJsonPropertyEquals(string property, string expected)
    {
        using var doc = JsonDocument.Parse(LastBody ?? "{}");
        Assert.True(TryGetProperty(doc.RootElement, property, out var value), $"响应 JSON 缺少属性 {property}");
        Assert.Equal(expected, value.GetString());
    }

    /// <summary>按点路径（如 user.email）逐级取属性；任一级缺失即返回 false。</summary>
    private static bool TryGetProperty(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }
        return true;
    }

    // ── 辅助 ────────────────────────────────────────────────────────
    private string RoleToken(string role) => role switch
    {
        "admin" => TokenCache["admin"],
        "T2 用户" => TokenCache["t2"],
        "成员" => TokenCache["member"],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "未知角色"),
    };

    private void CacheToken(string key, Func<Task<string>> login)
        => TokenCache.AddOrUpdate(key, _ => login().GetAwaiter().GetResult(), (_, _) => TokenCache[key]);

    private async Task SendAsync(string? bearer, string method, string url, string? docString)
    {
        object? body = null;
        if (!string.IsNullOrWhiteSpace(docString))
        {
            // 将 Gherkin DocString 解析为 JSON 元素，避免作为裸字符串被二次序列化为 JSON 字符串。
            using var doc = JsonDocument.Parse(docString);
            body = doc.RootElement.Clone();
        }

        var response = await IntegrationClient.SendAsync(new HttpMethod(method), url, bearer, body);
        _scenario["LastResponse"] = response;
        _scenario["LastBody"] = await response.ReadBodyAsync();
    }

    private HttpResponseMessage LastResponse => (HttpResponseMessage)_scenario["LastResponse"]!;
    private string LastBody => (string)_scenario["LastBody"]!;
}
