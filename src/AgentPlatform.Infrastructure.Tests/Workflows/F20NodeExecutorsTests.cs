#nullable disable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Workflows;

// ───────────────────────── Jint 条件求值器（S2，Jint 4.x API） ─────────────────────────

public class JsConditionEvaluatorTests
{
    private static readonly IConditionEvaluator Evaluator =
        new JsConditionEvaluator(Substitute.For<ILogger<JsConditionEvaluator>>());

    [Fact]
    public async Task EvaluateAsync_ArithmeticTrue_ReturnsTrue()
    {
        var result = await Evaluator.EvaluateAsync("1 + 1 === 2", new Dictionary<string, string>(), new Dictionary<string, string>(), null);
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateAsync_ArithmeticFalse_ReturnsFalse()
    {
        var result = await Evaluator.EvaluateAsync("1 > 2", new Dictionary<string, string>(), new Dictionary<string, string>(), null);
        Assert.False(result);
    }

    [Fact]
    public async Task EvaluateAsync_ReadsArtifactsAndBlackboardAndInput()
    {
        var artifacts = new Dictionary<string, string> { ["foo"] = "bar" };
        var blackboard = new Dictionary<string, string> { ["baz"] = "qux" };
        var result = await Evaluator.EvaluateAsync(
            "artifacts.foo === 'bar' && blackboard.baz === 'qux' && input === 'hi'",
            artifacts, blackboard, "hi");
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateAsync_NonBooleanCoercedByJsTruthiness()
    {
        // 非空字符串在 JS 中为 truthy
        var result = await Evaluator.EvaluateAsync("'hello'", new Dictionary<string, string>(), new Dictionary<string, string>(), null);
        Assert.True(result);
    }

    [Fact]
    public async Task EvaluateAsync_SyntaxError_ThrowsWorkflowExpressionException()
    {
        await Assert.ThrowsAsync<WorkflowExpressionException>(() =>
            Evaluator.EvaluateAsync("1 +", new Dictionary<string, string>(), new Dictionary<string, string>(), null));
    }

    [Fact]
    public async Task EvaluateAsync_InfiniteLoop_IsBounded_ThrowsWorkflowExpressionException()
    {
        // while(true){} 会触及 MaxStatements / TimeoutInterval 边界，被包装为 WorkflowExpressionException。
        await Assert.ThrowsAsync<WorkflowExpressionException>(() =>
            Evaluator.EvaluateAsync("while(true){}", new Dictionary<string, string>(), new Dictionary<string, string>(), null));
    }
}

// ───────────────────────── 变量节点（跨节点 Blackboard 读写） ─────────────────────────

public class VariableStepExecutorTests
{
    private static WorkflowContext Ctx()
    {
        return new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = Guid.NewGuid(),
        };
    }

    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns("变量节点");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    [Fact]
    public async Task ExecuteAsync_SetThenGet_SharesBlackboard()
    {
        var ctx = Ctx();
        var exec = new VariableStepExecutor(Substitute.For<ILogger<VariableStepExecutor>>());

        var setResult = await exec.ExecuteAsync(Node("{\"mode\":\"set\",\"name\":\"x\",\"value\":\"hello\"}"), ctx);
        Assert.Equal(StepOutcome.Success, setResult.Outcome);
        Assert.Equal("hello", ctx.Blackboard.Get("x"));

        var getResult = await exec.ExecuteAsync(Node("{\"mode\":\"get\",\"name\":\"x\"}"), ctx);
        Assert.Equal("hello", getResult.Output);
    }

    [Fact]
    public async Task ExecuteAsync_SetWithPlaceholder_SubstitutesFromArtifact()
    {
        var ctx = new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>
            {
                ["src"] = new StepArtifact { StepName = "src", StepOrder = 0, Content = "world", ContentType = "general" },
            },
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = Guid.NewGuid(),
        };
        var exec = new VariableStepExecutor(Substitute.For<ILogger<VariableStepExecutor>>());

        var result = await exec.ExecuteAsync(Node("{\"mode\":\"set\",\"name\":\"y\",\"value\":\"{{src}}\"}"), ctx);
        Assert.Equal("world", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_MissingName_ReturnsFatalFailure()
    {
        var exec = new VariableStepExecutor(Substitute.For<ILogger<VariableStepExecutor>>());
        var result = await exec.ExecuteAsync(Node("{\"mode\":\"set\"}"), Ctx());
        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
    }
}

// ───────────────────────── 延迟节点 ─────────────────────────

public class DelayStepExecutorTests
{
    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns("延迟节点");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    [Fact]
    public async Task ExecuteAsync_SmallDelay_ReturnsSuccess()
    {
        var exec = new DelayStepExecutor(Substitute.For<ILogger<DelayStepExecutor>>());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await exec.ExecuteAsync(Node("{\"durationMs\":5}"), null);
        sw.Stop();
        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConfig_WaitsZero()
    {
        var exec = new DelayStepExecutor(Substitute.For<ILogger<DelayStepExecutor>>());
        var result = await exec.ExecuteAsync(Node("{}"), null);
        Assert.Equal(StepOutcome.Success, result.Outcome);
    }
}

// ───────────────────────── HTTP 节点（真实出站 + 占位替换） ─────────────────────────

public class HttpStepExecutorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage LastRequest;
        public string LastRequestBody;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_respond(request));
        }
    }

    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns("HTTP 节点");
        step.ConfigJson.Returns(configJson);
        return step;
    }

    private static WorkflowContext Ctx()
    {
        return new WorkflowContext
        {
            WorkflowId = Guid.NewGuid(),
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = Guid.NewGuid(),
        };
    }

    [Fact]
    public async Task ExecuteAsync_Get200_ReturnsSuccessWithBody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("workflow-http").Returns(new HttpClient(handler));

        var exec = new HttpStepExecutor(Substitute.For<ILogger<HttpStepExecutor>>(), factory);
        var result = await exec.ExecuteAsync(Node("{\"method\":\"GET\",\"url\":\"https://api.test/x\"}"), Ctx());

        Assert.Equal(StepOutcome.Success, result.Outcome);
        Assert.Contains("ok", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_SubstitutesBodyPlaceholder_FromBlackboard()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("workflow-http").Returns(new HttpClient(handler));

        var ctx = Ctx();
        ctx.Blackboard.Set("token", "abc123");

        var exec = new HttpStepExecutor(Substitute.For<ILogger<HttpStepExecutor>>(), factory);
        await exec.ExecuteAsync(Node("{\"method\":\"POST\",\"url\":\"https://api.test/x\",\"bodyTemplate\":\"Bearer {{token}}\"}"), ctx);

        var body = handler.LastRequestBody;
        Assert.Contains("Bearer abc123", body);
    }

    [Fact]
    public async Task ExecuteAsync_NonSuccessStatus_ReturnsRetryableFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("workflow-http").Returns(new HttpClient(handler));

        var exec = new HttpStepExecutor(Substitute.For<ILogger<HttpStepExecutor>>(), factory);
        var result = await exec.ExecuteAsync(Node("{\"method\":\"GET\",\"url\":\"https://api.test/x\"}"), Ctx());

        Assert.Equal(StepOutcome.FailedRetry, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_MissingUrl_ReturnsFatalFailure()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var exec = new HttpStepExecutor(Substitute.For<ILogger<HttpStepExecutor>>(), factory);
        var result = await exec.ExecuteAsync(Node("{}"), Ctx());
        Assert.Equal(StepOutcome.FailedRollback, result.Outcome);
    }
}

// ───────────────────────── 人工审批门节点（HITL 创建 + 重入防御） ─────────────────────────

public class UserInputStepExecutorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Wf = Guid.NewGuid();
    private const string NodeName = "审批节点";

    private static IWorkflowExecutable Node(string configJson)
    {
        var step = Substitute.For<IWorkflowExecutable>();
        step.Name.Returns(NodeName);
        step.ConfigJson.Returns(configJson);
        return step;
    }

    private static WorkflowContext Ctx()
    {
        return new WorkflowContext
        {
            WorkflowId = Wf,
            CurrentStepOrder = 0,
            Artifacts = new Dictionary<string, StepArtifact>(),
            Blackboard = Blackboard.Empty,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = Tenant,
        };
    }

    [Fact]
    public async Task ExecuteAsync_NoPending_CreatesApprovalAndNeedsIntervention()
    {
        var repo = Substitute.For<IHumanApprovalRepository>();
        repo.GetPendingByNodeAsync(Tenant, Wf, NodeName, Arg.Any<CancellationToken>()).Returns((HumanApproval)null);
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(Tenant);

        var exec = new UserInputStepExecutor(Substitute.For<ILogger<UserInputStepExecutor>>(), repo, uow, tenant);
        var result = await exec.ExecuteAsync(Node("{\"prompt\":\"请审批\"}"), Ctx());

        Assert.Equal(StepOutcome.NeedsIntervention, result.Outcome);
        repo.Received(1).Add(Arg.Any<HumanApproval>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExistingPending_ReusesAndDoesNotCreate()
    {
        var existing = new HumanApproval(Guid.NewGuid(), Tenant, Wf, NodeName, "请审批");
        var repo = Substitute.For<IHumanApprovalRepository>();
        repo.GetPendingByNodeAsync(Tenant, Wf, NodeName, Arg.Any<CancellationToken>()).Returns(existing);
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(Tenant);

        var exec = new UserInputStepExecutor(Substitute.For<ILogger<UserInputStepExecutor>>(), repo, uow, tenant);
        var result = await exec.ExecuteAsync(Node("{\"prompt\":\"请审批\"}"), Ctx());

        Assert.Equal(StepOutcome.NeedsIntervention, result.Outcome);
        repo.DidNotReceive().Add(Arg.Any<HumanApproval>());
    }
}

// ───────────────────────── HumanApproval 聚合（领域规则） ─────────────────────────

public class HumanApprovalTests
{
    [Fact]
    public void Constructor_DefaultsToPending()
    {
        var a = new HumanApproval(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "n", "p");
        Assert.Equal(HumanApprovalStatus.Pending, a.Status);
    }

    [Fact]
    public void Approve_SetsApprovedAndInput()
    {
        var a = new HumanApproval(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "n", "p");
        a.Approve("yes");
        Assert.Equal(HumanApprovalStatus.Approved, a.Status);
        Assert.Equal("yes", a.SubmittedInput);
        Assert.NotNull(a.ResolvedAt);
    }

    [Fact]
    public void Reject_SetsRejectedAndReason()
    {
        var a = new HumanApproval(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "n", "p");
        a.Reject("nope");
        Assert.Equal(HumanApprovalStatus.Rejected, a.Status);
        Assert.Equal("nope", a.SubmittedInput);
    }

    [Fact]
    public void Approve_WhenAlreadyResolved_Throws()
    {
        var a = new HumanApproval(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "n", "p");
        a.Approve("yes");
        Assert.Throws<InvalidOperationException>(() => a.Reject("late"));
    }
}
