using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// Custom Agent Role BDD 步骤 —— 真 HTTP + 真 DB（设计文档 §7：POST /api/v1/agent-roles）。
/// 移除旧版 InMemoryAgentRoleRepository 假仓库，全部经 HttpClient 走真实管线 + 真实文件 SQLite。
/// 认证使用 T1 种子 admin 的 JWT（Bearer），经 ITenantProvider 解析为 T1，查询仅见 T1 数据。
/// </summary>
[Binding]
public class CustomAgentRoleSteps
{
    private string _adminToken = "";
    private string _lastRoleCode = "";
    private string _lastRoleSystemPrompt = "";
    private string _lastAttemptedRoleCode = "";
    private AgentResponseDto? _lastAgent;
    private readonly List<string> _createdRoleCodes = new();
    private HttpStatusCode _lastStatusCode;

    private async Task EnsureAdminAsync() => _adminToken = await IntegrationClient.AdminTokenAsync();

    [Given("the agent role management system is initialized")]
    public void GivenSystemInitialized() { /* 基础种子已由 DatabaseInitializer 在 Integration 环境完成 */ }

    /// <summary>
    /// Background 重置：清空 T1 自建 agent 与自定义（非内置）角色，保证每个 Scenario 起始状态确定，
    /// 避免「角色已存在 / 角色被 agent 引用导致删除被拒」等跨场景污染。
    /// </summary>
    [Given("the agent role store is reset")]
    public async Task GivenStoreReset()
    {
        using var scope = IntegrationHost.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agents = await db.Set<Agent>().IgnoreQueryFilters()
            .Where(a => a.TenantId == IntegrationConstants.Tenant1Id).ToListAsync();
        db.Set<Agent>().RemoveRange(agents);
        var customRoles = await db.Set<AgentRoleDefinition>().IgnoreQueryFilters()
            .Where(r => !r.IsBuiltIn).ToListAsync();
        db.Set<AgentRoleDefinition>().RemoveRange(customRoles);
        await db.SaveChangesAsync();
    }

    [When("a user creates an agent role with:")]
    public async Task WhenUserCreatesAgentRoleWith(Table table)
    {
        await EnsureAdminAsync();
        var fields = table.Rows.ToDictionary(r => r["Field"], r => r["Value"]);
        var payload = new
        {
            name = fields["Name"],
            roleCode = fields["RoleCode"],
            description = fields["Description"],
            systemPrompt = fields["SystemPrompt"],
        };
        _lastRoleCode = payload.roleCode;
        _lastRoleSystemPrompt = payload.systemPrompt;
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-roles", _adminToken, payload);
        _lastStatusCode = resp.StatusCode;
    }

    [Then("the role should be saved")]
    public void ThenRoleShouldBeSaved()
    {
        Assert.Equal(HttpStatusCode.OK, _lastStatusCode);
        Assert.False(string.IsNullOrWhiteSpace(_lastRoleCode));
    }

    [Then(@"the role should be queryable by role code ""(.*)""")]
    public async Task ThenRoleQueryableByCode(string roleCode)
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-roles/{roleCode}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var role = await IntegrationClient.ReadAsAsync<AgentRoleResponseDto>(resp);
        Assert.NotNull(role);
        Assert.Equal(roleCode, role!.RoleCode);
    }

    [Given(@"a custom role ""(.*)"" exists")]
    public async Task GivenCustomRoleExists(string name)
    {
        await EnsureAdminAsync();
        _lastRoleCode = DeriveCode(name);
        _lastRoleSystemPrompt = $"System prompt for {name}.";
        var payload = new
        {
            name,
            roleCode = _lastRoleCode,
            description = $"{name} description.",
            systemPrompt = _lastRoleSystemPrompt,
        };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-roles", _adminToken, payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        if (!_createdRoleCodes.Contains(_lastRoleCode))
            _createdRoleCodes.Add(_lastRoleCode);
    }

    [When("a user creates an agent with that role")]
    public async Task WhenUserCreatesAgentWithThatRole()
    {
        await EnsureAdminAsync();
        var payload = new
        {
            name = "Agent using custom role",
            roleCode = _lastRoleCode,
            modelProvider = "openai",
            modelName = "gpt-4o",
            modelApiUrl = "https://api.openai.com/v1",
            systemPrompt = _lastRoleSystemPrompt,
        };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agents", _adminToken, payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastAgent = await IntegrationClient.ReadAsAsync<AgentResponseDto>(resp);
    }

    [Then("the agent should use the custom role's system prompt")]
    public void ThenAgentUsesCustomRoleSystemPrompt()
    {
        Assert.NotNull(_lastAgent);
        Assert.Equal(_lastRoleSystemPrompt, _lastAgent!.SystemPrompt);
        Assert.Equal(_lastRoleCode, _lastAgent.RoleCode);
    }

    [Given("3 custom roles exist")]
    public async Task GivenThreeCustomRolesExist()
    {
        await EnsureAdminAsync();
        foreach (var code in new[] { "role-alpha", "role-beta", "role-gamma" })
        {
            var payload = new
            {
                name = $"Role {code}",
                roleCode = code,
                description = $"Description for {code}.",
                systemPrompt = $"Prompt for {code}.",
            };
            var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-roles", _adminToken, payload);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            if (!_createdRoleCodes.Contains(code))
                _createdRoleCodes.Add(code);
        }
    }

    [When("a user lists all available roles")]
    public async Task WhenUserListsRoles()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/agent-roles", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _listedRoles = await IntegrationClient.ReadAsAsync<List<AgentRoleSummaryDto>>(resp);
    }

    private List<AgentRoleSummaryDto>? _listedRoles;

    [Then("the system should return all 3 roles")]
    public void ThenReturnedAllThreeRoles()
    {
        Assert.NotNull(_listedRoles);
        foreach (var code in new[] { "role-alpha", "role-beta", "role-gamma" })
            Assert.Contains(_listedRoles!, r => r.RoleCode == code);
    }

    [Then("each role should include its Name, RoleCode, and Description")]
    public void ThenEachRoleHasFields()
    {
        Assert.NotNull(_listedRoles);
        foreach (var role in _listedRoles!)
        {
            Assert.False(string.IsNullOrWhiteSpace(role.Name));
            Assert.False(string.IsNullOrWhiteSpace(role.RoleCode));
            Assert.NotNull(role.Description);
        }
    }

    [When("a user deletes the role")]
    public async Task WhenUserDeletesRole()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Delete, $"/api/v1/agent-roles/{_lastRoleCode}", _adminToken);
        _lastStatusCode = resp.StatusCode;
    }

    [Then("the role should no longer be queryable")]
    public async Task ThenRoleNotQueryable()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-roles/{_lastRoleCode}", _adminToken);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [When("a user creates an agent with the role")]
    public async Task WhenUserCreatesAgentWithRole()
    {
        await EnsureAdminAsync();
        var payload = new
        {
            name = "Agent bound to role",
            roleCode = _lastRoleCode,
            modelProvider = "openai",
            modelName = "gpt-4o",
            modelApiUrl = "https://api.openai.com/v1",
            systemPrompt = "bound prompt",
        };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agents", _adminToken, payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Then("the delete should be rejected because the role is in use")]
    public async Task ThenDeleteRejectedInUse()
    {
        Assert.Equal(HttpStatusCode.Conflict, _lastStatusCode);
        // 角色仍应可查询（未被删除）
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-roles/{_lastRoleCode}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [When("a user creates an agent role with empty name")]
    public async Task WhenCreateRoleEmptyName()
    {
        await EnsureAdminAsync();
        _lastAttemptedRoleCode = "empty-name-role";
        var payload = new
        {
            name = "",
            roleCode = _lastAttemptedRoleCode,
            description = "desc",
            systemPrompt = "prompt",
        };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/agent-roles", _adminToken, payload);
        _lastStatusCode = resp.StatusCode;
    }

    [Then("the system should return a validation error")]
    public void ThenValidationError() => Assert.Equal(HttpStatusCode.BadRequest, _lastStatusCode);

    [Then("the role should not be created")]
    public async Task ThenRoleNotCreated()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/agent-roles/{_lastAttemptedRoleCode}", _adminToken);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private static string DeriveCode(string name) => name.ToLowerInvariant().Replace(" ", "-");
}
