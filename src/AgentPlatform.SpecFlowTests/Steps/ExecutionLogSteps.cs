using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
using AgentPlatform.Domain.Enums;
using Reqnroll;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

/// <summary>
/// Execution Log BDD 步骤 —— 真 HTTP + 真 DB（设计文档 §7：GET /api/v1/execution-logs）。
/// 移除旧版 InMemoryExecutionLogRepository 假仓库：执行日志经 ExecutionLogSeeder 真实落库
/// （文件 SQLite），再经 HTTP 查询端点断言过滤 / 分页行为。所有种子落 T1，admin(T1) 查询可见。
/// </summary>
[Binding]
public class ExecutionLogSteps
{
    private string _adminToken = "";
    private ExecutionLogListResponseDto? _lastList;
    private ExecutionLogStepsResponseDto? _lastSteps;
    private Guid _lastLogId;

    private async Task EnsureAdminAsync() => _adminToken = await IntegrationClient.AdminTokenAsync();

    private static ExecutionLog MakeCompletedLog(Guid id, string wfName, int steps, bool failed = false)
    {
        var log = new ExecutionLog(id, Guid.NewGuid(), wfName, IntegrationConstants.Tenant1Id, steps);
        for (int i = 0; i < steps; i++)
            log.AddEntry(new ExecutionLogEntry(Guid.NewGuid(), $"Step {i + 1}", i, WorkflowState.Completed, TimeSpan.FromMilliseconds(120), "ok", null));
        if (failed) log.Fail(); else log.Complete();
        return log;
    }

    [Given("the execution log store is reset")]
    public async Task GivenStoreReset() => await ExecutionLogSeeder.ClearAsync();

    [Given("3 workflow executions have completed")]
    public async Task GivenThreeCompleted()
    {
        var logs = new[]
        {
            MakeCompletedLog(Guid.NewGuid(), "WF A", 1),
            MakeCompletedLog(Guid.NewGuid(), "WF B", 1),
            MakeCompletedLog(Guid.NewGuid(), "WF C", 1),
        };
        await ExecutionLogSeeder.SeedAsync(logs);
    }

    [When("a user queries execution logs for the workflow")]
    public async Task WhenQueryLogs()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(HttpMethod.Get, "/api/v1/execution-logs", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastList = await IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(resp);
    }

    [Then("they should receive 3 log entries")]
    public void ThenThreeEntries()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(3, _lastList!.TotalCount);
        Assert.Equal(3, _lastList.Items.Count);
    }

    [Then("each entry should contain status, duration, and timestamp")]
    public void ThenEntryFields()
    {
        Assert.NotNull(_lastList);
        foreach (var e in _lastList!.Items)
        {
            Assert.True(Enum.IsDefined(typeof(WorkflowState), e.Status));
            Assert.True(e.StartedAt != default);
        }
    }

    [Given("step 2 of the workflow failed")]
    public async Task GivenStep2Failed()
    {
        _lastLogId = Guid.NewGuid();
        var log = new ExecutionLog(_lastLogId, Guid.NewGuid(), "WF With Failure", IntegrationConstants.Tenant1Id, 3);
        log.AddEntry(new ExecutionLogEntry(Guid.NewGuid(), "Step 1", 0, WorkflowState.Completed, TimeSpan.FromMilliseconds(100), "ok", null));
        log.AddEntry(new ExecutionLogEntry(Guid.NewGuid(), "Step 2", 1, WorkflowState.Failed, TimeSpan.FromMilliseconds(200), null, "Step 2 failed: downstream timeout"));
        log.AddEntry(new ExecutionLogEntry(Guid.NewGuid(), "Step 3", 2, WorkflowState.Completed, TimeSpan.FromMilliseconds(100), "ok", null));
        log.Fail();
        await ExecutionLogSeeder.SeedAsync(new[] { log });
    }

    [When("a user queries execution logs")]
    public async Task WhenQueryFailedSteps()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/execution-logs/{_lastLogId}/steps?status=Failed", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastSteps = await IntegrationClient.ReadAsAsync<ExecutionLogStepsResponseDto>(resp);
    }

    [Then("the log entry for step 2 should include error details")]
    public void ThenStep2ErrorDetails()
    {
        Assert.NotNull(_lastSteps);
        var failed = _lastSteps!.Items.SingleOrDefault(s => s.StepName == "Step 2");
        Assert.NotNull(failed);
        Assert.Equal((int)WorkflowState.Failed, failed!.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.ErrorDetail));
    }

    [Then("the error message should describe the failure reason")]
    public void ThenErrorMessageDescribes()
    {
        Assert.NotNull(_lastSteps);
        var failed = _lastSteps!.Items.SingleOrDefault(s => s.StepName == "Step 2");
        Assert.NotNull(failed);
        Assert.Contains("timeout", failed!.ErrorDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Given("execution logs exist")]
    public async Task GivenLogsExist() => await GivenThreeCompleted();

    [When("a user filters logs by a future date range")]
    public async Task WhenFilterFutureRange()
    {
        await EnsureAdminAsync();
        var from = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd");
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/execution-logs?from={from}&to={to}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastList = await IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(resp);
    }

    [Then("no logs should be returned")]
    public void ThenNoLogs()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(0, _lastList!.TotalCount);
    }

    [When("a user filters logs by a range covering today")]
    public async Task WhenFilterTodayRange()
    {
        await EnsureAdminAsync();
        var from = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/execution-logs?from={from}&to={to}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastList = await IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(resp);
    }

    [Then("all logs within that range should be returned")]
    public void ThenAllInRange()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(3, _lastList!.TotalCount);
    }

    [Given("some executions succeeded and some failed")]
    public async Task GivenMixed()
    {
        var logs = new[]
        {
            MakeCompletedLog(Guid.NewGuid(), "WF Ok", 1),
            MakeCompletedLog(Guid.NewGuid(), "WF Bad", 1, failed: true),
        };
        await ExecutionLogSeeder.SeedAsync(logs);
    }

    [When(@"a user filters logs by status ""(.*)""")]
    public async Task WhenFilterByStatus(string status)
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, $"/api/v1/execution-logs?status={status}", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastList = await IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(resp);
    }

    [Then("only failed execution entries should be returned")]
    public void ThenOnlyFailed()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(1, _lastList!.TotalCount);
        Assert.All(_lastList.Items, i => Assert.Equal((int)WorkflowState.Failed, i.Status));
    }

    [Given("50 execution logs exist")]
    public async Task GivenFifty()
    {
        var logs = new List<ExecutionLog>();
        for (int i = 0; i < 50; i++)
            logs.Add(MakeCompletedLog(Guid.NewGuid(), $"WF {i}", 1));
        await ExecutionLogSeeder.SeedAsync(logs);
    }

    [When("a user queries with page 1 and page size 20")]
    public async Task WhenPaginate()
    {
        await EnsureAdminAsync();
        var resp = await IntegrationClient.SendAsync(
            HttpMethod.Get, "/api/v1/execution-logs?skip=0&take=20", _adminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        _lastList = await IntegrationClient.ReadAsAsync<ExecutionLogListResponseDto>(resp);
    }

    [Then("they should receive 20 entries")]
    public void ThenTwenty()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(20, _lastList!.Items.Count);
    }

    [Then("total count should be 50")]
    public void ThenTotalFifty()
    {
        Assert.NotNull(_lastList);
        Assert.Equal(50, _lastList!.TotalCount);
    }
}
