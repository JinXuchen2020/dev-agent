using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 智能体与智能体配置 BDD 步骤（B8）：创建（捕获动态 id）、列表、详情 404、删除 404、
/// RBAC（仅 Admin 可写/删/模板）、租户隔离。所有请求经真实管线 <see cref="IntegrationHost.Api"/>。
/// 同时登记 Given/When/Then 三种关键字。
/// </summary>
[Binding]
public sealed class AgentsSteps
{
    private readonly ScenarioContext _scenario;

    public AgentsSteps(ScenarioContext scenario) => _scenario = scenario;

    private const string ValidConfigYaml = """
        agent_role: developer
        system_prompt: "You are a helpful BDD test agent."
        model:
          provider: openai
          name: gpt-4o
          api_url: https://api.openai.com/v1
        """;

    private static string AdminToken()
        => AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword)
            .GetAwaiter().GetResult();

    private static string MemberToken()
        => AuthHelper.LoginAsync(IntegrationConstants.NonAdminEmail, IntegrationConstants.NonAdminPassword)
            .GetAwaiter().GetResult();

    private static string T2Token()
        => AuthHelper.LoginAsync(
            IntegrationConstants.Tenant2UserEmail,
            IntegrationConstants.Tenant2UserPassword,
            IntegrationConstants.Tenant2Id).GetAwaiter().GetResult();

    private Guid AgentId => (Guid)_scenario["AgentId"];
    private Guid ConfigId => (Guid)_scenario["ConfigId"];

    // ── 智能体 ──────────────────────────────────────────────────────
    [Given("^以 admin 身份创建智能体 \"([^\"]*)\"$")]
    [When("^以 admin 身份创建智能体 \"([^\"]*)\"$")]
    [Then("^以 admin 身份创建智能体 \"([^\"]*)\"$")]
    public async Task CreateAgent(string name)
    {
        var body = new { name, roleCode = "development" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agents", AdminToken(), body);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        _scenario["AgentId"] = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
    }

    [Given("^以 成员 身份创建智能体$")]
    [When("^以 成员 身份创建智能体$")]
    [Then("^以 成员 身份创建智能体$")]
    public async Task CreateAgentAsMember()
    {
        var body = new { name = "member-agent", roleCode = "development" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agents", MemberToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 成员 身份列出智能体$")]
    [When("^以 成员 身份列出智能体$")]
    [Then("^以 成员 身份列出智能体$")]
    public async Task ListAgentsAsMember()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/agents", MemberToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份获取不存在的智能体$")]
    [When("^以 admin 身份获取不存在的智能体$")]
    [Then("^以 admin 身份获取不存在的智能体$")]
    public async Task GetMissingAgent()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agents/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份删除不存在的智能体$")]
    [When("^以 admin 身份删除不存在的智能体$")]
    [Then("^以 admin 身份删除不存在的智能体$")]
    public async Task DeleteMissingAgent()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Delete, $"/api/v1/agents/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 T2 用户身份列出智能体$")]
    [When("^以 T2 用户身份列出智能体$")]
    [Then("^以 T2 用户身份列出智能体$")]
    public async Task ListAgentsAsT2()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/agents", T2Token());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^T2 智能体列表不含该智能体 id$")]
    [When("^T2 智能体列表不含该智能体 id$")]
    [Then("^T2 智能体列表不含该智能体 id$")]
    public void T2ListExcludesAgent()
        => Assert.DoesNotContain(AgentId.ToString(), (string)_scenario["LastBody"]!);

    // ── 智能体配置 ──────────────────────────────────────────────────
    [Given("^以 admin 身份创建智能体配置 \"([^\"]*)\"$")]
    [When("^以 admin 身份创建智能体配置 \"([^\"]*)\"$")]
    [Then("^以 admin 身份创建智能体配置 \"([^\"]*)\"$")]
    public async Task CreateConfig(string name)
    {
        var body = new { name, yamlContent = ValidConfigYaml, description = "BDD config", agentTypeCode = (string?)null };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-configurations", AdminToken(), body);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        _scenario["ConfigId"] = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
    }

    [Given("^以 成员 身份创建智能体配置$")]
    [When("^以 成员 身份创建智能体配置$")]
    [Then("^以 成员 身份创建智能体配置$")]
    public async Task CreateConfigAsMember()
    {
        var body = new { name = "member-config", yamlContent = ValidConfigYaml };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-configurations", MemberToken(), body);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份列出智能体配置$")]
    [When("^以 admin 身份列出智能体配置$")]
    [Then("^以 admin 身份列出智能体配置$")]
    public async Task ListConfigs()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/agent-configurations", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份获取不存在的智能体配置$")]
    [When("^以 admin 身份获取不存在的智能体配置$")]
    [Then("^以 admin 身份获取不存在的智能体配置$")]
    public async Task GetMissingConfig()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-configurations/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 成员 身份获取配置模板$")]
    [When("^以 成员 身份获取配置模板$")]
    [Then("^以 成员 身份获取配置模板$")]
    public async Task GetTemplateAsMember()
    {
        var id = (Guid)_scenario["ConfigId"];
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-configurations/{id}/template", MemberToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份获取不存在的配置模板$")]
    [When("^以 admin 身份获取不存在的配置模板$")]
    [Then("^以 admin 身份获取不存在的配置模板$")]
    public async Task GetMissingTemplate()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-configurations/{missing}/template", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }
}
