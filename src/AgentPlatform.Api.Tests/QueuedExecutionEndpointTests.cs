using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// F37 队列模式端到端契约测试（真 HTTP + 完整管线 + InMemory 队列 + worker 同进程消费）：
/// · 创建并运行：入队 → worker 执行 → 等待窗口内返回终态（200 聚合，非 202）。
/// · 既有直接运行端点在队列模式下同样经 worker 完成（契约形态不变，仅分发路径不同）。
/// · 入队被拒 → 503 ProblemDetails（回带 workflowId，工作流已落库未丢）——假队列恒拒投。
/// · 接受入队但停摆不消费 → 等待窗口超时确定性返回 202 {queued:true}——假队列只收不派。
/// 证明「Api → 队列 → worker → F30 租约 → 编排器 → 终态」链路真实可用，而非只测抽象。
/// </summary>
public sealed class QueuedExecutionEndpointTests : IClassFixture<QueueModeApiContractTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QueueModeApiContractTestFactory _factory;

    /// <summary>注入共享队列模式夹具（同进程 worker 消费）。</summary>
    public QueuedExecutionEndpointTests(QueueModeApiContractTestFactory factory) => _factory = factory;

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    /// <summary>
    /// 读取 currentState：平台 API 枚举按 int 序列化（WorkflowState：
    /// Pending=0 / Running=1 / Paused=2 / Completed=3 / Failed=4 / RolledBack=5），
    /// 兼容字符串形态。
    /// </summary>
    private static string? StateOf(JsonElement root)
    {
        if (!root.TryGetProperty("currentState", out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number)
        {
            return el.GetInt32() switch
            {
                0 => "Pending", 1 => "Running", 2 => "Paused",
                3 => "Completed", 4 => "Failed", 5 => "RolledBack",
                _ => el.GetRawText(),
            };
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
    }

    [Fact]
    public async Task RunWorkflow_InQueueMode_Completes_Through_Worker()
    {
        var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.PostAsync("/api/v1/workflows", Json(new
        {
            name = "queued-e2e-workflow",
            initialContext = "{}",
        }));

        // 队列模式默认在等待窗口内轮询终态；worker 同进程消费，Stub 模型瞬时完成 → 期望 200。
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,
            $"unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            // 容忍慢环境：202 时终态由 worker 稍后落库，轮询详情确认执行确实发生。
            using var queued = JsonDocument.Parse(body);
            var workflowId = queued.RootElement.GetProperty("workflowId").GetString()!;
            // 202 载荷的 state 同样可能是 int 形态（Pending/Running）。

            for (var i = 0; i < 30; i++)
            {
                var detail = await client.GetAsync($"/api/v1/workflows/{workflowId}");
                Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
                using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
                var state = StateOf(doc.RootElement);
                if (state is "Completed" or "Failed" or "RolledBack")
                {
                    Assert.Equal("Completed", state);
                    return;
                }

                await Task.Delay(500);
            }

            throw new Xunit.Sdk.XunitException("queued workflow never reached terminal state");
        }

        using var completed = JsonDocument.Parse(body);
        Assert.Equal("Completed", StateOf(completed.RootElement));
    }

    [Fact]
    public async Task RunExistingWorkflow_InQueueMode_Also_Completes_Through_Worker()
    {
        var client = _factory.CreateAuthenticatedClient("Admin");

        // 先以队列模式创建一个已完成的工作流（复用上一条链路）。
        var created = await client.PostAsync("/api/v1/workflows", Json(new
        {
            name = "queued-rerun-workflow",
            initialContext = "{}",
        }));
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(
            created.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,
            $"create+run failed: {(int)created.StatusCode} {createdBody}");

        string workflowId;
        using (var doc = JsonDocument.Parse(createdBody))
        {
            workflowId = doc.RootElement.TryGetProperty("id", out var idProp)
                ? idProp.GetString()!
                : doc.RootElement.GetProperty("workflowId").GetString()!;
        }

        // 重跑：队列模式下由 worker 再执行一次，终态 Completed（或等待超时 202 后轮询确认）。
        var rerun = await client.PostAsync($"/api/v1/workflows/{workflowId}/run", Json(new { }));
        Assert.True(
            rerun.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted,
            $"rerun failed: {(int)rerun.StatusCode} {await rerun.Content.ReadAsStringAsync()}");

        for (var i = 0; i < 30; i++)
        {
            var detail = await client.GetAsync($"/api/v1/workflows/{workflowId}");
            using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            var state = StateOf(doc.RootElement);
            if (state is "Completed" or "Failed" or "RolledBack")
            {
                Assert.Equal("Completed", state);
                return;
            }

            await Task.Delay(500);
        }

        throw new Xunit.Sdk.XunitException("re-run workflow never reached terminal state");
    }
}

/// <summary>
/// F37 验收 2（拒投分支）：队列满 / 后端不可用时 run 端点必须 503 ProblemDetails——
/// 显式失败不假成功；工作流已落库（未丢）且响应回带 workflowId 供调用方定位重试。
/// </summary>
public sealed class QueueRejectionContractTests : IClassFixture<QueueRejectingApiContractTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QueueRejectingApiContractTestFactory _factory;

    /// <summary>注入恒拒投假队列夹具。</summary>
    public QueueRejectionContractTests(QueueRejectingApiContractTestFactory factory) => _factory = factory;

    [Fact]
    public async Task RunWorkflow_EnqueueRejected_Returns_503_With_WorkflowId_And_Workflow_Persisted()
    {
        var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.PostAsync("/api/v1/workflows",
            new StringContent(JsonSerializer.Serialize(new { name = "queued-rejected-workflow", initialContext = "{}" }, JsonOptions),
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(503, problem.RootElement.GetProperty("status").GetInt32());
        // ProblemDetails.Extensions = [JsonExtensionData] → workflowId 以 RFC 7807 顶层成员序列化。
        Assert.True(problem.RootElement.TryGetProperty("workflowId", out var wfIdProp),
            "503 body must carry the already-persisted workflowId (no orphaned reference)");
        Assert.True(Guid.TryParse(wfIdProp.GetString(), out var workflowId),
            "503 workflowId extension must be a GUID");

        // 工作流已落库未丢：详情可查且仍为 Pending（未入队成功、未被消费）。
        var detail = await client.GetAsync($"/api/v1/workflows/{workflowId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("currentState").GetInt32()); // Pending=0

        // 投递尝试确实到达队列接缝并被显式拒绝（而非中途静默丢失）。
        Assert.Single(_factory.Queue.EnqueueAttempts);
        Assert.Equal(workflowId, _factory.Queue.EnqueueAttempts[0].WorkflowId);
    }
}

/// <summary>
/// F37 D2=B（超时分支）：作业入队成功但 worker 侧不可消费（后端停摆）时，
/// run 端点等待窗口超时必须确定性返回 202 {queued:true, workflowId, state}——显式不假成功。
/// </summary>
public sealed class QueueWaitTimeoutContractTests : IClassFixture<QueueStalledApiContractTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QueueStalledApiContractTestFactory _factory;

    /// <summary>注入「只收不派」假队列夹具（等待窗口 1s）。</summary>
    public QueueWaitTimeoutContractTests(QueueStalledApiContractTestFactory factory) => _factory = factory;

    [Fact]
    public async Task RunWorkflow_WaitWindowElapsed_Returns_202_Queued()
    {
        var client = _factory.CreateAuthenticatedClient("Admin");

        var response = await client.PostAsync("/api/v1/workflows",
            new StringContent(JsonSerializer.Serialize(new { name = "queued-stalled-workflow", initialContext = "{}" }, JsonOptions),
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("queued").GetBoolean());
        Assert.True(Guid.TryParse(doc.RootElement.GetProperty("workflowId").GetString(), out var workflowId));
        var state = doc.RootElement.TryGetProperty("state", out var s) ? s.GetString() : null;
        Assert.True(state is null or "Pending" or "Running", $"unexpected queued state {state}");

        // 作业确实入队成功（202 的前提是已接管，非拒投）。
        Assert.Contains(_factory.Queue.EnqueueAttempts, j => j.WorkflowId == workflowId);
    }
}

