using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// F21 联调冒烟：在完整 ASP.NET Core 管线（真实控制器 → MediatR → EF in-memory SQLite →
/// Stub 模型编排）上，以已鉴权 + 匿名两种身份跑通三种触发器（Webhook / Schedule / Chat）的
/// 正向与边界路径。覆盖：webhook 令牌生成/启用/禁用/调用、坏令牌 404、schedule upsert 与查询、
/// 会话绑定/查询/触发/解绑 以及 chatBindingCount 计数。
/// </summary>
public sealed class WorkflowTriggersIntegrationTests : IClassFixture<ApiContractTestFactory>
{
    private readonly ApiContractTestFactory _factory;

    public WorkflowTriggersIntegrationTests(ApiContractTestFactory factory)
    {
        _factory = factory;
    }

    private static async Task<Guid> CreateWorkflowAsync(HttpClient client)
    {
        var body = JsonSerializer.Serialize(new { name = "F21-Smoke", initialContext = "{}" });
        var res = await client.PostAsync(
            "/api/v1/workflows",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client)
    {
        var res = await client.PostAsync("/api/v1/conversations", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task WebhookTrigger_FullLifecycle_ReturnsOk()
    {
        var admin = _factory.CreateAuthenticatedClient("Admin");
        var wfId = await CreateWorkflowAsync(admin);

        // 1) 生成 webhook 令牌（首次创建）
        var genRes = await admin.PostAsync($"/api/v1/workflows/{wfId}/triggers/webhook", null);
        Assert.Equal(HttpStatusCode.OK, genRes.StatusCode);
        var genDoc = JsonDocument.Parse(await genRes.Content.ReadAsStringAsync());
        var token = genDoc.RootElement.GetProperty("triggerToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(genDoc.RootElement.GetProperty("created").GetBoolean());

        // 2) 已鉴权查询触发器骨架：webhook 存在且启用，schedule 为 null，chat=0
        var getRes = await admin.GetAsync($"/api/v1/workflows/{wfId}/triggers");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getDoc = JsonDocument.Parse(await getRes.Content.ReadAsStringAsync());
        var root = getDoc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.GetProperty("webhook").ValueKind);
        Assert.True(root.GetProperty("webhook").GetProperty("enabled").GetBoolean());
        Assert.False(root.TryGetProperty("schedule", out _));
        Assert.Equal(0, root.GetProperty("chatBindingCount").GetInt32());

        // 3) 匿名调用有效令牌 → 200，workflowId 匹配
        var anon = _factory.CreateClient();
        var invokeRes = await anon.PostAsync(
            $"/api/v1/webhooks/workflow/{token}",
            new StringContent("{\"input\":\"hi\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, invokeRes.StatusCode);
        var invokeDoc = JsonDocument.Parse(await invokeRes.Content.ReadAsStringAsync());
        Assert.Equal(wfId, invokeDoc.RootElement.GetProperty("workflowId").GetGuid());

        // 4) 匿名调用坏令牌 → 404（不泄露存在性）
        var badRes = await anon.PostAsync(
            "/api/v1/webhooks/workflow/nonexistent-token-xyz",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, badRes.StatusCode);

        // 5) 禁用 webhook → 200 enabled=false
        var delRes = await admin.DeleteAsync($"/api/v1/workflows/{wfId}/triggers/webhook");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
        Assert.False(JsonDocument.Parse(await delRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("enabled").GetBoolean());

        // 6) 禁用后匿名调用原令牌 → 404
        var disabledRes = await anon.PostAsync(
            $"/api/v1/webhooks/workflow/{token}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, disabledRes.StatusCode);

        // 7) 禁用后查询：webhook 仍存在但 enabled=false（令牌保留、失效，可重新启用），chat=0
        var get2 = await admin.GetAsync($"/api/v1/workflows/{wfId}/triggers");
        var get2Doc = JsonDocument.Parse(await get2.Content.ReadAsStringAsync());
        Assert.True(get2Doc.RootElement.TryGetProperty("webhook", out var wh2));
        Assert.False(wh2.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, get2Doc.RootElement.GetProperty("chatBindingCount").GetInt32());
    }

    [Fact]
    public async Task ScheduleTrigger_PutAndGet_ReturnsOk()
    {
        var admin = _factory.CreateAuthenticatedClient("Admin");
        var wfId = await CreateWorkflowAsync(admin);

        // PUT schedule（cron + 时区 + 启用）
        var putBody = JsonSerializer.Serialize(new
        {
            cron = "0 0 * * *",
            timezone = "Asia/Shanghai",
            enabled = true
        });
        var putRes = await admin.PutAsync(
            $"/api/v1/workflows/{wfId}/triggers/schedule",
            new StringContent(putBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var putDoc = JsonDocument.Parse(await putRes.Content.ReadAsStringAsync());
        Assert.Equal("0 0 * * *", putDoc.RootElement.GetProperty("cron").GetString());
        Assert.Equal("Asia/Shanghai", putDoc.RootElement.GetProperty("timezone").GetString());
        Assert.True(putDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.String, putDoc.RootElement.GetProperty("nextRunAt").ValueKind);

        // GET triggers：schedule 存在，webhook 为 null（无令牌），chat=0
        var getRes = await admin.GetAsync($"/api/v1/workflows/{wfId}/triggers");
        var getDoc = JsonDocument.Parse(await getRes.Content.ReadAsStringAsync());
        var root = getDoc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.GetProperty("schedule").ValueKind);
        Assert.Equal("0 0 * * *", root.GetProperty("schedule").GetProperty("cron").GetString());
        Assert.False(root.TryGetProperty("webhook", out _));
        Assert.Equal(0, root.GetProperty("chatBindingCount").GetInt32());

        // 边界：cron 为空 → 400
        var badBody = JsonSerializer.Serialize(new { cron = "", timezone = "UTC", enabled = true });
        var badRes = await admin.PutAsync(
            $"/api/v1/workflows/{wfId}/triggers/schedule",
            new StringContent(badBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task ConversationChatTrigger_BindTriggerUnbind_ReturnsOk()
    {
        var admin = _factory.CreateAuthenticatedClient("Admin");
        var wfId = await CreateWorkflowAsync(admin);
        var convId = await CreateConversationAsync(admin);

        // 绑定会话到工作流
        var bindBody = JsonSerializer.Serialize(new { workflowId = wfId });
        var bindRes = await admin.PostAsync(
            $"/api/v1/conversations/{convId}/workflow-bindings",
            new StringContent(bindBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, bindRes.StatusCode);

        // 查询绑定列表：包含 wfId
        var listRes = await admin.GetAsync($"/api/v1/conversations/{convId}/workflow-bindings");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listDoc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
        var found = false;
        foreach (var item in listDoc.RootElement.EnumerateArray())
        {
            if (item.GetProperty("workflowId").GetGuid() == wfId)
            {
                found = true;
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("workflowName").GetString()));
            }
        }
        Assert.True(found, "bound workflow should appear in listing");

        // 工作流触发器查询：chatBindingCount == 1
        var trigRes = await admin.GetAsync($"/api/v1/workflows/{wfId}/triggers");
        Assert.Equal(1, JsonDocument.Parse(await trigRes.Content.ReadAsStringAsync())
            .RootElement.GetProperty("chatBindingCount").GetInt32());

        // 在会话上下文中触发绑定工作流 → 200，workflowId 匹配
        var runRes = await admin.PostAsync(
            $"/api/v1/conversations/{convId}/trigger-workflow/{wfId}", null);
        Assert.Equal(HttpStatusCode.OK, runRes.StatusCode);
        var runDoc = JsonDocument.Parse(await runRes.Content.ReadAsStringAsync());
        Assert.Equal(wfId, runDoc.RootElement.GetProperty("workflowId").GetGuid());

        // 触发一个未绑定的工作流 → 404
        var otherWf = await CreateWorkflowAsync(admin);
        var notBound = await admin.PostAsync(
            $"/api/v1/conversations/{convId}/trigger-workflow/{otherWf}", null);
        Assert.Equal(HttpStatusCode.NotFound, notBound.StatusCode);

        // 解绑 → 204
        var unbindRes = await admin.DeleteAsync(
            $"/api/v1/conversations/{convId}/workflow-bindings/{wfId}");
        Assert.Equal(HttpStatusCode.NoContent, unbindRes.StatusCode);

        // 解绑后 chatBindingCount 回到 0
        var trigRes2 = await admin.GetAsync($"/api/v1/workflows/{wfId}/triggers");
        Assert.Equal(0, JsonDocument.Parse(await trigRes2.Content.ReadAsStringAsync())
            .RootElement.GetProperty("chatBindingCount").GetInt32());
    }
}
