using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AgentPlatform.Domain.Enums;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// F22 发布工作流为 API/MCP Server 的 BDD 集成步骤：全部经真实 HttpClient 走完整管线，
/// 连真实文件 SQLite。无 mock Repository、无 in-memory（设计文档 §4.3）。
///
/// 绑定字符串均以 ^...$ 锚定，确保 Reqnroll 按正则表达式解析（而非 Cucumber Expression），
/// 避免把步骤文本中的特殊字符误判为表达式语法。
/// </summary>
[Binding]
public sealed class PublishedWorkflowSteps
{
    private readonly HttpClient _api = IntegrationHost.Api;
    private string? _slug;
    private HttpResponseMessage? _lastResponse;
    private string? _lastBody;

    // ── Background ────────────────────────────────────────────────
    [Given("^集成租户 T1 下存在一个 Completed 状态的工作流 W1$")]
    public void GivenSampleWorkflowW1() { /* 由 IntegrationSeeder 种子（SampleWorkflowId）*/ }

    [Given("^集成租户 T1 持有一个有效的 ApiKey$")]
    public void GivenT1ApiKey() { /* 由 IntegrationSeeder 种子（T1ApiKeyId）*/ }

    // ── 发布为 API 模式并生成 slug ────────────────────────────────
    [When("^发布 W1 为 API 模式$")]
    public async Task WhenPublishW1Api()
    {
        _slug = await PublishAsync(IntegrationConstants.SampleWorkflowId, PublishMode.Api, null);
    }

    [Then("^响应 200 且返回 16 位 URL 安全 slug$")]
    public void ThenPublishReturnsSlug()
    {
        Assert.Equal(HttpStatusCode.OK, _lastResponse!.StatusCode);
        Assert.NotNull(_slug);
        Assert.Equal(16, _slug.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", _slug); // URL 安全字符
    }

    [Then("^查询 W1 发布状态为 Enabled$")]
    public async Task ThenStatusEnabled()
    {
        var jwt = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/workflows/{IntegrationConstants.SampleWorkflowId}/publish")
            .WithBearer(jwt);
        _lastResponse = await _api.SendAsync(req);
        _lastBody = await _lastResponse.ReadBodyAsync();

        Assert.Equal(HttpStatusCode.OK, _lastResponse.StatusCode);
        using var doc = JsonDocument.Parse(_lastBody!);
        Assert.True(doc.RootElement.TryGetProperty("isEnabled", out var enabled) && enabled.GetBoolean());
    }

    // ── 用绑定 Key 经 slug 运行 ────────────────────────────────────
    [Given("^W1 已发布为 Api 模式并绑定 T1 Key$")]
    public async Task GivenW1PublishedApiBound()
    {
        _slug = await PublishAsync(IntegrationConstants.SampleWorkflowId, PublishMode.Api, IntegrationConstants.T1ApiKeyId);
    }

    [When("^带 ApiKey 调用 slug 运行并附输入$")]
    public async Task WhenRunWithT1Key()
    {
        _lastResponse = await RunAsync(_slug!, IntegrationConstants.T1ApiKeyPlaintext, "{\"topic\":\"hello\"}");
        _lastBody = await _lastResponse.ReadBodyAsync();
    }

    [Then("^响应 200 且返回工作流最终输出$")]
    public void ThenRunReturnsOutput()
    {
        Assert.Equal(HttpStatusCode.OK, _lastResponse!.StatusCode);
        using var doc = JsonDocument.Parse(_lastBody!);
        Assert.True(doc.RootElement.TryGetProperty("output", out var output));
        Assert.False(string.IsNullOrEmpty(output.GetString()), "运行应返回非空最终输出。");
    }

    // ── 错误 Key 被拒 ──────────────────────────────────────────────
    [Given("^W1 已发布并绑定 T1 Key$")]
    public async Task GivenW1PublishedBound()
    {
        _slug = await PublishAsync(IntegrationConstants.SampleWorkflowId, PublishMode.Api, IntegrationConstants.T1ApiKeyId);
    }

    [When("^用 T2 的 Key 调用 slug 运行$")]
    public async Task WhenRunWithT2Key()
    {
        _lastResponse = await RunAsync(_slug!, IntegrationConstants.T2ApiKeyPlaintext, null);
        _lastBody = await _lastResponse.ReadBodyAsync();
    }

    [Then("^响应 404$")]
    public void ThenNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, _lastResponse!.StatusCode);
    }

    // ── 跨租户不可运行他人发布 ─────────────────────────────────────
    [Given("^租户 T2 发布了 W2，Api 模式，T2 Key$")]
    public async Task GivenT2PublishedW2()
    {
        _slug = await PublishAsTenantAsync(
            IntegrationConstants.Tenant2UserEmail, IntegrationConstants.Tenant2UserPassword,
            IntegrationConstants.SampleWorkflow2Id, PublishMode.Api, IntegrationConstants.T2ApiKeyId);
    }

    [When("^租户 T1 用自身 Key 调用 W2 的 slug$")]
    public async Task WhenT1RunsW2()
    {
        _lastResponse = await RunAsync(_slug!, IntegrationConstants.T1ApiKeyPlaintext, null);
        _lastBody = await _lastResponse.ReadBodyAsync();
    }

    // ── MCP tools/list 过滤 ────────────────────────────────────────
    [Given("^W1 发布为 Mcp 模式并启用$")]
    public async Task GivenW1PublishedMcp()
    {
        _slug = await PublishAsync(IntegrationConstants.SampleWorkflowId, PublishMode.Mcp, null);
    }

    [Given("^W3 发布为 Api 模式，应被列表排除$")]
    public async Task GivenW3PublishedApi()
    {
        await PublishAsync(IntegrationConstants.SampleWorkflow3Id, PublishMode.Api, null);
    }

    [When("^带 ApiKey 发送 MCP tools list 请求$")]
    public async Task WhenMcpToolsList()
    {
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }.WithApiKey(IntegrationConstants.T1ApiKeyPlaintext);
        _lastResponse = await _api.SendAsync(req);
        _lastBody = await _lastResponse.ReadBodyAsync();
    }

    [Then("^tools 列表仅含 W1$")]
    public void ThenToolsListOnlyW1()
    {
        Assert.Equal(HttpStatusCode.OK, _lastResponse!.StatusCode);
        using var doc = JsonDocument.Parse(_lastBody!);
        var tools = doc.RootElement
            .GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .ToList();

        Assert.Contains(_slug!, tools);
        Assert.DoesNotContain(tools, name => name != _slug); // 仅 W1（其 name = slug）
    }

    // ── 取消发布后 slug 不可用 ─────────────────────────────────────
    [Given("^W1 已发布$")]
    public async Task GivenW1Published()
    {
        _slug = await PublishAsync(IntegrationConstants.SampleWorkflowId, PublishMode.Api, null);
    }

    [When("^取消发布 W1$")]
    public async Task WhenUnpublishW1()
    {
        var jwt = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/workflows/{IntegrationConstants.SampleWorkflowId}/publish")
            .WithBearer(jwt);
        _lastResponse = await _api.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, _lastResponse.StatusCode);
    }

    [Then("^再调用 slug 端点返回 404$")]
    public async Task ThenSlugReturns404()
    {
        _lastResponse = await RunAsync(_slug!, IntegrationConstants.T1ApiKeyPlaintext, null);
        _lastBody = await _lastResponse.ReadBodyAsync();
        Assert.Equal(HttpStatusCode.NotFound, _lastResponse.StatusCode);
    }

    // ── 辅助：发布（T1 admin JWT）─────────────────────────────────
    // mode 传 PublishMode 枚举，序列化时转为整数（0=Api,1=Mcp），
    // 与 API 默认 JSON 选项（无 JsonStringEnumConverter）一致。
    private async Task<string> PublishAsync(Guid workflowId, PublishMode mode, Guid? apiKeyId)
    {
        var jwt = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        var payload = JsonSerializer.Serialize(new
        {
            mode,
            apiKeyId = apiKeyId?.ToString()
        });
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflows/{workflowId}/publish")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }.WithBearer(jwt);

        _lastResponse = await _api.SendAsync(req);
        _lastBody = await _lastResponse.ReadBodyAsync();
        Assert.Equal(HttpStatusCode.OK, _lastResponse.StatusCode);

        using var doc = JsonDocument.Parse(_lastBody!);
        return doc.RootElement.GetProperty("slug").GetString()!;
    }

    // ── 辅助：以指定租户身份发布（跨租户场景）─────────────────────
    private async Task<string> PublishAsTenantAsync(string email, string password, Guid workflowId, PublishMode mode, Guid? apiKeyId)
    {
        var jwt = await AuthHelper.LoginAsync(email, password, IntegrationConstants.Tenant2Id);
        var payload = JsonSerializer.Serialize(new { mode, apiKeyId = apiKeyId?.ToString() });
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflows/{workflowId}/publish")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }.WithBearer(jwt);

        _lastResponse = await _api.SendAsync(req);
        _lastBody = await _lastResponse.ReadBodyAsync();
        Assert.Equal(HttpStatusCode.OK, _lastResponse.StatusCode);

        using var doc = JsonDocument.Parse(_lastBody!);
        return doc.RootElement.GetProperty("slug").GetString()!;
    }

    // ── 辅助：运行（ApiKey）───────────────────────────────────────
    private async Task<HttpResponseMessage> RunAsync(string slug, string apiKey, string? inputJson)
    {
        var payload = JsonSerializer.Serialize(new { inputJson });
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/published-workflows/{slug}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }.WithApiKey(apiKey);
        return await _api.SendAsync(req);
    }
}
