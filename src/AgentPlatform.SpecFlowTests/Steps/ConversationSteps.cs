using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 会话 + 聊天 BDD 步骤（B4）：创建会话（捕获动态 id）、发消息、列表、详情 404、
/// 成本报告 RBAC、工作流绑定（Chat 触发器）与跨租户/未绑定 404。
/// 所有请求经真实管线 <see cref="IntegrationHost.Api"/>，复用 <see cref="AuthHelper"/> 登录。
/// 动态 id 在运行时拼接，避免 Gherkin 静态 URL 无法携带运行期 id。
/// 同时登记 Given/When/Then 三种关键字以支持 And/But 推导后的任意关键字。
/// </summary>
[Binding]
public sealed class ConversationSteps
{
    private readonly ScenarioContext _scenario;

    public ConversationSteps(ScenarioContext scenario) => _scenario = scenario;

    private static string AdminToken()
        => AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword)
            .GetAwaiter().GetResult();

    private static string MemberToken()
        => AuthHelper.LoginAsync(IntegrationConstants.NonAdminEmail, IntegrationConstants.NonAdminPassword)
            .GetAwaiter().GetResult();

    private Guid ConvId => (Guid)_scenario["ConvId"];

    [Given("^以 admin 身份创建会话$")]
    [When("^以 admin 身份创建会话$")]
    [Then("^以 admin 身份创建会话$")]
    public async Task CreateConversation()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/conversations", AdminToken());
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        _scenario["ConvId"] = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
    }

    [Given("^以 成员 身份创建会话$")]
    [When("^以 成员 身份创建会话$")]
    [Then("^以 成员 身份创建会话$")]
    public async Task CreateConversationAsMember()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/conversations", MemberToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 成员 身份列出会话$")]
    [When("^以 成员 身份列出会话$")]
    [Then("^以 成员 身份列出会话$")]
    public async Task ListConversationsAsMember()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/conversations", MemberToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份获取不存在的会话$")]
    [When("^以 admin 身份获取不存在的会话$")]
    [Then("^以 admin 身份获取不存在的会话$")]
    public async Task GetMissingConversation()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/conversations/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^向该会话发送消息 \"([^\"]*)\"$")]
    [When("^向该会话发送消息 \"([^\"]*)\"$")]
    [Then("^向该会话发送消息 \"([^\"]*)\"$")]
    public async Task SendMessage(string content)
    {
        var body = new { content };
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/conversations/{ConvId}/messages", AdminToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 成员 身份访问成本报告$")]
    [When("^以 成员 身份访问成本报告$")]
    [Then("^以 成员 身份访问成本报告$")]
    public async Task CostReportAsMember()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/conversations/cost-report", MemberToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份访问成本报告$")]
    [When("^以 admin 身份访问成本报告$")]
    [Then("^以 admin 身份访问成本报告$")]
    public async Task CostReportAsAdmin()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/conversations/cost-report", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^该会话绑定种子工作流$")]
    [When("^该会话绑定种子工作流$")]
    [Then("^该会话绑定种子工作流$")]
    public async Task BindSeedWorkflow()
    {
        var body = new { workflowId = IntegrationConstants.SampleWorkflowId };
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/conversations/{ConvId}/workflow-bindings", AdminToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^列出该会话的工作流绑定$")]
    [When("^列出该会话的工作流绑定$")]
    [Then("^列出该会话的工作流绑定$")]
    public async Task ListBindings()
    {
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/conversations/{ConvId}/workflow-bindings", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^触发该会话未绑定的工作流$")]
    [When("^触发该会话未绑定的工作流$")]
    [Then("^触发该会话未绑定的工作流$")]
    public async Task TriggerUnboundWorkflow()
    {
        var unbound = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/conversations/{ConvId}/trigger-workflow/{unbound}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }
}
