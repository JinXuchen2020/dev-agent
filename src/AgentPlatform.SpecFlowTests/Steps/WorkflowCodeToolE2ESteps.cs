using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// F12 BDD 步骤：起真实后端（保留真实 Code/Tool 执行器），导入含 Tool(真实 HTTP) / Code(真实 python
/// 子进程) 节点的工作流并运行，断言端到端 stdout / 响应回填与节点状态（features/tool-code-e2e.md）。
///
/// 所有请求经真实管线 <see cref="F12IntegrationHost.Api"/>（RealStepsIntegrationAppFactory），
/// 连真实文件 SQLite（test-integration-f12.db）。认证走真实登录端点拿 JWT（不伪造 token）。
/// </summary>
[Binding]
public sealed class WorkflowCodeToolE2ESteps
{
    private readonly ScenarioContext _scenario;

    public WorkflowCodeToolE2ESteps(ScenarioContext scenario)
    {
        _scenario = scenario;
    }

    private string AdminToken => (string)_scenario["AdminToken"];

    private WorkflowDetailResponseDto LastRun => (WorkflowDetailResponseDto)_scenario["LastRun"];

    private Guid ImportedWorkflowId => (Guid)_scenario["ImportedWorkflowId"];

    [Given("the F12 real-executor host is initialized")]
    public void GivenHostInitialized()
    {
        // 生命周期由 F12IntegrationHooks.[Before/After]TestRun 管理；此处仅为语义占位。
    }

    [Given("^I am logged in as T1 admin$")]
    [When("^I am logged in as T1 admin$")]
    [Then("^I am logged in as T1 admin$")]
    public async Task GivenLoggedInAsAdmin()
    {
        var token = await AuthHelper.LoginAsync(IntegrationConstants.AdminEmail, IntegrationConstants.AdminPassword);
        _scenario["AdminToken"] = token;
    }

    [When("^I import a workflow with Start, Code, Tool, End nodes via the F12 API$")]
    public async Task WhenImportWorkflow()
    {
        var startId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var endId = Guid.NewGuid();

        // 枚举按整数序列化：Start=0, End=1, Tool=6, Code=7（API 未注册 JsonStringEnumConverter）。
        var request = new
        {
            name = "F12 Tool/Code E2E",
            initialContext = "{\"preset\":\"sequential\"}",
            nodes = new object[]
            {
                new { id = startId, type = 0, name = "Start", position = new { x = 0, y = 0 } },
                new
                {
                    id = codeId,
                    type = 7,
                    name = "RunCode",
                    position = new { x = 1, y = 0 },
                    config = "{\"code\":\"print('hello-from-code')\",\"language\":\"python\",\"timeoutSeconds\":30}",
                },
                new
                {
                    id = toolId,
                    type = 6,
                    name = "CallTool",
                    position = new { x = 2, y = 0 },
                    config = "{\"toolName\":\"bdd-echo-tool\",\"parameters\":{\"httpMethod\":\"GET\"}}",
                },
                new { id = endId, type = 1, name = "End", position = new { x = 3, y = 0 } },
            },
            edges = new object[]
            {
                new { id = Guid.NewGuid(), source = startId, target = codeId },
                new { id = Guid.NewGuid(), source = codeId, target = toolId },
                new { id = Guid.NewGuid(), source = toolId, target = endId },
            },
        };

        var resp = await F12IntegrationClient.SendAsync(HttpMethod.Post, "/api/v1/workflows/import", AdminToken, request);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await F12IntegrationClient.ReadAsAsync<WorkflowDetailResponseDto>(resp);
        Assert.NotNull(detail);
        _scenario["ImportedWorkflowId"] = detail!.Id;
        _scenario["LastRun"] = detail;
    }

    [When("^I run the imported workflow via the F12 API$")]
    public async Task WhenRunWorkflow()
    {
        var resp = await F12IntegrationClient.SendAsync(
            HttpMethod.Post, $"/api/v1/workflows/{ImportedWorkflowId}/run", AdminToken, new { preset = 0 });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var detail = await F12IntegrationClient.ReadAsAsync<WorkflowDetailResponseDto>(resp);
        Assert.NotNull(detail);
        _scenario["LastRun"] = detail!;
    }

    private static string DetailDump(WorkflowDetailResponseDto d)
        => System.Text.Json.JsonSerializer.Serialize(d, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    [Then("^the Code node result should contain \"([^\"]*)\"$")]
    public void ThenCodeNodeResultContains(string fragment)
    {
        var node = LastRun.Nodes!.First(n => n.Type == 7);
        if (!(node.Result ?? string.Empty).Contains(fragment))
            throw new Exception($"CODE NODE RESULT MISSING '{fragment}'.\nNODES DUMP:\n{DetailDump(LastRun)}");
    }

    [Then("^the Tool node result should contain \"([^\"]*)\"$")]
    public void ThenToolNodeResultContains(string fragment)
    {
        var node = LastRun.Nodes!.First(n => n.Type == 6);
        if (!(node.Result ?? string.Empty).Contains(fragment))
            throw new Exception($"TOOL NODE RESULT MISSING '{fragment}'.\nNODES DUMP:\n{DetailDump(LastRun)}");
    }

    [Then("^each graph node state should be Completed \\(3\\)$")]
    public void ThenAllNodesCompleted()
    {
        Assert.NotNull(LastRun.Nodes);
        Assert.NotEmpty(LastRun.Nodes);
        // Start(0)/End(1) 是控制标记节点：编排器不解析执行器、不改其 State，合法保持 Pending。
        // 仅可执行节点（Code=7 / Tool=6）应被标为 Completed(3)。
        var executable = LastRun.Nodes!.Where(n => n.Type is not (0 or 1)).ToList();
        Assert.NotEmpty(executable);
        foreach (var n in executable)
            if (n.State != 3)
                throw new Exception($"EXECUTABLE NODE {n.Name} (type {n.Type}) state={n.State} not Completed(3).\nNODES DUMP:\n{DetailDump(LastRun)}");
    }

    [When("^I query execution logs for the workflow via the F12 API$")]
    public async Task WhenQueryExecutionLogs()
    {
        var listResp = await F12IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/execution-logs", AdminToken);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await F12IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(listResp);
        Assert.NotNull(list);

        var log = list!.Items.FirstOrDefault(e => e.WorkflowId == ImportedWorkflowId);
        Assert.NotNull(log);
        _scenario["F12LogId"] = log!.Id;

        var stepsResp = await F12IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/execution-logs/{log.Id}/steps", AdminToken);
        Assert.Equal(HttpStatusCode.OK, stepsResp.StatusCode);
        var steps = await F12IntegrationClient.ReadAsAsync<ExecutionLogStepsResponseDto>(stepsResp);
        Assert.NotNull(steps);
        _scenario["F12LogSteps"] = steps!;
    }

    [Then("^the execution log should contain a step with result containing \"([^\"]*)\"$")]
    public void ThenLogStepContains(string fragment)
    {
        var steps = (ExecutionLogStepsResponseDto)_scenario["F12LogSteps"];
        if (!steps.Items.Any(s => (s.Result ?? string.Empty).Contains(fragment)))
            throw new Exception($"NO LOG STEP CONTAINS '{fragment}'.\nSTEPS DUMP:\n{System.Text.Json.JsonSerializer.Serialize(steps, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}");
    }
}
