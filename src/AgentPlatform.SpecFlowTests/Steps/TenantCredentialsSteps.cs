using System.Net;
using System.Net.Http;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// F13/F14 租户凭据 BDD 步骤：新增模型凭据（捕获 Id）、租户隔离断言。
/// 复用 <see cref="AuthHelper"/> 真实登录 + <see cref="IntegrationClient"/> 真管线。
/// 同时登记 Given/When/Then 以支持作为 And/But 推导后的任意关键字。
/// </summary>
[Binding]
public sealed class TenantCredentialsSteps
{
    private readonly ScenarioContext _scenario;

    public TenantCredentialsSteps(ScenarioContext scenario) => _scenario = scenario;

    [Given("^以 admin 身份新增一条模型凭据$")]
    [When("^以 admin 身份新增一条模型凭据$")]
    [Then("^以 admin 身份新增一条模型凭据$")]
    public async Task CreateModelCredential()
    {
        var token = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        var body = new
        {
            category = 0, // CredentialCategory.Model
            name = "BDD Model Credential",
            provider = "OpenAI",
            apiKey = "sk-bdd-test-key-not-real",
            baseUrl = (string?)null,
            modelName = "gpt-4o",
            isEnabled = true,
        };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/tenant/credentials", token, body);
        var text = await resp.ReadBodyAsync();
        _scenario["CredResp"] = resp;
        _scenario["CredBody"] = text!;
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
        using var doc = JsonDocument.Parse(text!);
        _scenario["CreatedCredId"] = doc.RootElement.GetProperty("id").GetString();
    }

    [Given("^返回的密钥为掩码形式$")]
    [When("^返回的密钥为掩码形式$")]
    [Then("^返回的密钥为掩码形式$")]
    public void ThenMaskedKey()
    {
        using var doc = JsonDocument.Parse((string)_scenario["CredBody"]);
        var key = doc.RootElement.GetProperty("apiKeyMask").GetString();
        Assert.NotNull(key);
        Assert.StartsWith("••••", key!);
    }

    [Given("^T2 的模型凭据列表不含该凭据$")]
    [When("^T2 的模型凭据列表不含该凭据$")]
    [Then("^T2 的模型凭据列表不含该凭据$")]
    public async Task ThenT2Excludes()
    {
        var t2 = await AuthHelper.LoginAsync(
            IntegrationConstants.Tenant2UserEmail, IntegrationConstants.Tenant2UserPassword, IntegrationConstants.Tenant2Id);
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/tenant/credentials?category=0", t2);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        var id = (string)_scenario["CreatedCredId"];
        Assert.DoesNotContain(doc.RootElement.EnumerateArray(), e => e.GetProperty("id").GetString() == id);
    }

    [Given("^T1 的模型凭据列表含该凭据$")]
    [When("^T1 的模型凭据列表含该凭据$")]
    [Then("^T1 的模型凭据列表含该凭据$")]
    public async Task ThenT1Includes()
    {
        var admin = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/tenant/credentials?category=0", admin);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        var id = (string)_scenario["CreatedCredId"];
        Assert.Contains(doc.RootElement.EnumerateArray(), e => e.GetProperty("id").GetString() == id);
    }
}
