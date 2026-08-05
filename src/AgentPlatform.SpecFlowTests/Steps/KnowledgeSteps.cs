using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// 知识库 BDD 步骤（B5）：创建（捕获动态 id）、列表、详情 404、删除 404、文档上传（multipart），
/// 以及租户隔离（T2 列表不含 T1 库）。所有请求经真实管线 <see cref="IntegrationHost.Api"/>。
/// 同时登记 Given/When/Then 三种关键字。
/// </summary>
[Binding]
public sealed class KnowledgeSteps
{
    private readonly ScenarioContext _scenario;

    public KnowledgeSteps(ScenarioContext scenario) => _scenario = scenario;

    private static string AdminToken()
        => AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword)
            .GetAwaiter().GetResult();

    private static string T2Token()
        => AuthHelper.LoginAsync(
            IntegrationConstants.Tenant2UserEmail,
            IntegrationConstants.Tenant2UserPassword,
            IntegrationConstants.Tenant2Id).GetAwaiter().GetResult();

    private Guid KbId => (Guid)_scenario["KbId"];

    [Given("^以 admin 身份创建知识库 \"([^\"]*)\"$")]
    [When("^以 admin 身份创建知识库 \"([^\"]*)\"$")]
    [Then("^以 admin 身份创建知识库 \"([^\"]*)\"$")]
    public async Task CreateKb(string name)
    {
        var body = new { name, description = "BDD knowledge base" };
        var resp = await IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/knowledge-bases", AdminToken(), body);
        var text = await resp.ReadBodyAsync();
        using var doc = JsonDocument.Parse(text!);
        _scenario["KbId"] = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = text!;
    }

    [Given("^以 admin 身份列出知识库$")]
    [When("^以 admin 身份列出知识库$")]
    [Then("^以 admin 身份列出知识库$")]
    public async Task ListKb()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/knowledge-bases", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份获取不存在的知识库$")]
    [When("^以 admin 身份获取不存在的知识库$")]
    [Then("^以 admin 身份获取不存在的知识库$")]
    public async Task GetMissingKb()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, $"/api/v1/knowledge-bases/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份删除不存在的知识库$")]
    [When("^以 admin 身份删除不存在的知识库$")]
    [Then("^以 admin 身份删除不存在的知识库$")]
    public async Task DeleteMissingKb()
    {
        var missing = Guid.NewGuid();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Delete, $"/api/v1/knowledge-bases/{missing}", AdminToken());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 admin 身份向该知识库上传文档$")]
    [When("^以 admin 身份向该知识库上传文档$")]
    [Then("^以 admin 身份向该知识库上传文档$")]
    public async Task UploadDocument()
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StringContent("This is a BDD test document about agent platforms.", Encoding.UTF8);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "bdd-test.txt");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/knowledge-bases/{KbId}/documents")
        {
            Content = content,
        };
        request.WithBearer(AdminToken());
        var resp = await IntegrationHost.Api.SendAsync(request);
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^以 T2 用户身份列出知识库$")]
    [When("^以 T2 用户身份列出知识库$")]
    [Then("^以 T2 用户身份列出知识库$")]
    public async Task ListKbAsT2()
    {
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/knowledge-bases", T2Token());
        _scenario["LastResponse"] = resp;
        _scenario["LastBody"] = await resp.ReadBodyAsync();
    }

    [Given("^T2 列表不含该知识库 id$")]
    [When("^T2 列表不含该知识库 id$")]
    [Then("^T2 列表不含该知识库 id$")]
    public void T2ListExcludesKb()
    {
        Assert.DoesNotContain(KbId.ToString(), (string)_scenario["LastBody"]!);
    }
}
