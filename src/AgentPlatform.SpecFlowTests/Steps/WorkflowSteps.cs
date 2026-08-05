using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 工作流管理 BDD 步骤：导入（捕获动态 id）、按租户读取、跨租户隔离、更新、版本、运行。
/// 所有请求经真实管线 <see cref="IntegrationHost.Api"/>，复用 <see cref="AuthHelper"/> 登录。
/// 动态 id 在运行时由 <c>WorkflowSteps</c> 拼接，避免 Gherkin 静态 URL 无法携带运行期 id。
/// 同时登记 Given/When/Then 以支持作为 And/But 推导后的任意关键字。
/// </summary>
[Binding]
public sealed class WorkflowSteps
{
    private readonly ScenarioContext _scenario;

    public WorkflowSteps(ScenarioContext scenario) => _scenario = scenario;

    private static string AdminToken()
        => AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword)
            .GetAwaiter().GetResult();

    private static string T2Token()
        => AuthHelper.LoginAsync(
            IntegrationConstants.Tenant2UserEmail,
            IntegrationConstants.Tenant2UserPassword,
            IntegrationConstants.Tenant2Id).GetAwaiter().GetResult();

    private static string MemberToken()
        => AuthHelper.LoginAsync(IntegrationConstants.NonAdminEmail, IntegrationConstants.NonAdminPassword)
            .GetAwaiter().GetResult();

    private Guid WfId => (Guid)_scenario["WfId"];

    [Given("^以 admin 身份导入一条工作流$")]
    [When("^以 admin 身份导入一条工作流$")]
    [Then("^以 admin 身份导入一条工作流$")]
    public async Task ImportWorkflow()
    {
        var body = new { name = "BDD Imported Workflow", initialContext = "{}" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/workflows/import", AdminToken(), body);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        _scenario["WfId"] = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
    }

    [Given("^以 admin 身份获取导入的工作流$")]
    [When("^以 admin 身份获取导入的工作流$")]
    [Then("^以 admin 身份获取导入的工作流$")]
    public async Task GetImported()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/workflows/{WfId}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 T2 用户身份获取导入的工作流$")]
    [When("^以 T2 用户身份获取导入的工作流$")]
    [Then("^以 T2 用户身份获取导入的工作流$")]
    public async Task GetImportedAsT2()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/workflows/{WfId}", T2Token());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份运行导入的工作流$")]
    [When("^以 admin 身份运行导入的工作流$")]
    [Then("^以 admin 身份运行导入的工作流$")]
    public async Task RunImported()
    {
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/workflows/{WfId}/run", AdminToken(), (object?)null);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份为导入的工作流创建版本$")]
    [When("^以 admin 身份为导入的工作流创建版本$")]
    [Then("^以 admin 身份为导入的工作流创建版本$")]
    public async Task CreateVersion()
    {
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/workflows/{WfId}/versions", AdminToken(), (object?)null);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份列出导入的工作流的版本$")]
    [When("^以 admin 身份列出导入的工作流的版本$")]
    [Then("^以 admin 身份列出导入的工作流的版本$")]
    public async Task ListVersions()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/workflows/{WfId}/versions", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份用新名称更新导入的工作流$")]
    [When("^以 admin 身份用新名称更新导入的工作流$")]
    [Then("^以 admin 身份用新名称更新导入的工作流$")]
    public async Task UpdateWithName()
    {
        var body = new { name = "BDD Imported Workflow (renamed)" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Put, $"/api/v1/workflows/{WfId}", AdminToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份空更新导入的工作流$")]
    [When("^以 admin 身份空更新导入的工作流$")]
    [Then("^以 admin 身份空更新导入的工作流$")]
    public async Task UpdateEmpty()
    {
        var body = new { };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Put, $"/api/v1/workflows/{WfId}", AdminToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 成员 身份更新导入的工作流$")]
    [When("^以 成员 身份更新导入的工作流$")]
    [Then("^以 成员 身份更新导入的工作流$")]
    public async Task UpdateAsMember()
    {
        var body = new { name = "attempted-by-member" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Put, $"/api/v1/workflows/{WfId}", MemberToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }
}
