using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// F22 控制器层集成测试：验证对外暴露的已发布工作流端点（API + MCP）在未携带
/// API Key 时必须返回 401，即外部调用鉴权边界生效。无需种子数据——401 在到达
/// handler 前由 ApiKey 认证 scheme 拦截。
/// </summary>
public sealed class PublishedWorkflowsEndpointTests : IClassFixture<ApiContractTestFactory>
{
    private readonly ApiContractTestFactory _factory;

    public PublishedWorkflowsEndpointTests(ApiContractTestFactory factory) => _factory = factory;

    [Fact]
    public async Task RunPublishedWorkflow_WithoutApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        using var content = new StringContent("{\"inputJson\":null}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/published-workflows/bogus-slug", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithoutApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        var rpc = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        });
        using var content = new StringContent(rpc, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/mcp", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
