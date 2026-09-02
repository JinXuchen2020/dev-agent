using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Application.Workflows;
using AgentPlatform.Application.Workflows.Commands.ExecuteQueuedWorkflow;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// F37 队列化执行（决策 D1=B/D2=B/D3=A/D4=A）Application 层单测：
/// · QueuedRunSupport：入队 + 等待终态 / 超时 202 / 拒投 503 三结局。
/// · ExecuteQueuedWorkflowCommand：载荷复现租户+工作空间上下文、跨租户拒执行、
///   运行中重复投递跳过、F30 租约冲突映射 Duplicate、触发投递走 FromQueue=true 防回环。
/// · TriggerWorkflowCommandHandler：QueueEnabled 外部触发投递队列（Pending 结果）、拒投降级直跑。
/// </summary>
public sealed class QueuedExecutionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static ExecutionJob BuildJob(
        Guid workflowId,
        int attempt = 1,
        int? triggerType = null,
        string? payloadJson = null) =>
        new(Guid.NewGuid(), workflowId, TenantId, WorkspaceId, (int)OrchestrationPreset.Sequential,
            triggerType, payloadJson, Attempt: attempt);

    // ── QueuedRunSupport ──

    [Fact]
    public async Task EnqueueAndWait_Completes_When_Workflow_Reaches_Terminal_State()
    {
        var queue = Substitute.For<IExecutionQueue>();
        queue.EnqueueAsync(Arg.Any<ExecutionJob>(), Arg.Any<CancellationToken>())
            .Returns(EnqueueResult.Enqueued);

        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        var repo = Substitute.For<IWorkflowRepository>();
        // F37 审查修复后等待方用 GetByIdFreshAsync（AsNoTracking 新鲜读）轮询。
        repo.GetByIdFreshAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        var settings = new DurableExecutionSettings { QueueWaitTimeoutSeconds = 4, QueuePollIntervalSeconds = 1 };
        var logger = Substitute.For<ILogger<AgentPlatform.Application.Workflows.Commands.RunWorkflow.RunWorkflowCommandHandler>>();

        var task = QueuedRunSupport.EnqueueAndWaitAsync(queue, repo, settings, BuildJob(wf.Id), logger, CancellationToken.None);
        // 模拟 worker 完成执行（轮询窗口内到达终态）。
        await Task.Delay(1500);
        wf.SetState(WorkflowState.Completed);

        var result = await task;

        Assert.Equal(QueueDispatchStatus.Completed, result.Dispatch);
        Assert.Same(wf, result.Workflow);
        await queue.Received(1).EnqueueAsync(Arg.Is<ExecutionJob>(j => j.WorkflowId == wf.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnqueueAndWait_Returns_Queued_On_Wait_Timeout()
    {
        var queue = Substitute.For<IExecutionQueue>();
        queue.EnqueueAsync(Arg.Any<ExecutionJob>(), Arg.Any<CancellationToken>())
            .Returns(EnqueueResult.Enqueued);

        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        wf.SetState(WorkflowState.Running); // 永不完成
        var repo = Substitute.For<IWorkflowRepository>();
        repo.GetByIdFreshAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        var settings = new DurableExecutionSettings { QueueWaitTimeoutSeconds = 2, QueuePollIntervalSeconds = 1 };
        var logger = Substitute.For<ILogger<AgentPlatform.Application.Workflows.Commands.RunWorkflow.RunWorkflowCommandHandler>>();

        var result = await QueuedRunSupport.EnqueueAndWaitAsync(queue, repo, settings, BuildJob(wf.Id), logger, CancellationToken.None);

        Assert.Equal(QueueDispatchStatus.Queued, result.Dispatch);
        Assert.Null(result.Workflow);
        Assert.Equal(WorkflowState.Running, result.State);
    }

    [Fact]
    public async Task EnqueueAndWait_Rejected_When_Enqueue_Fails()
    {
        var queue = Substitute.For<IExecutionQueue>();
        queue.EnqueueAsync(Arg.Any<ExecutionJob>(), Arg.Any<CancellationToken>())
            .Returns(EnqueueResult.RejectedQueueFull);

        var repo = Substitute.For<IWorkflowRepository>();
        var settings = new DurableExecutionSettings { QueueWaitTimeoutSeconds = 2, QueuePollIntervalSeconds = 1 };
        var logger = Substitute.For<ILogger<AgentPlatform.Application.Workflows.Commands.RunWorkflow.RunWorkflowCommandHandler>>();

        var result = await QueuedRunSupport.EnqueueAndWaitAsync(queue, repo, settings, BuildJob(Guid.NewGuid()), logger, CancellationToken.None);

        Assert.Equal(QueueDispatchStatus.Rejected, result.Dispatch);
        await repo.DidNotReceiveWithAnyArgs().GetByIdFreshAsync(default, default);
    }

    // ── ExecuteQueuedWorkflowCommand ──

    private static (ExecuteQueuedWorkflowCommandHandler Handler, IWorkflowRepository Repo, IOrchestrationPrimitive Primitive, ITenantContext Tenant, IWorkspaceContext Workspace, IMediator Mediator) BuildExecutor()
    {
        var repo = Substitute.For<IWorkflowRepository>();
        var primitive = Substitute.For<IOrchestrationPrimitive>();
        var audit = Substitute.For<IAuditLogRepository>();
        var tenant = Substitute.For<ITenantContext>();
        var workspace = Substitute.For<IWorkspaceContext>();
        var mediator = Substitute.For<IMediator>();
        var logger = Substitute.For<ILogger<ExecuteQueuedWorkflowCommandHandler>>();

        var handler = new ExecuteQueuedWorkflowCommandHandler(repo, primitive, audit, tenant, workspace, mediator, logger);
        return (handler, repo, primitive, tenant, workspace, mediator);
    }

    [Fact]
    public async Task Execute_Jobs_Sets_Tenant_And_Workspace_Overrides_And_Runs_Preset()
    {
        var (handler, repo, primitive, tenant, workspace, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.Executed, outcome);
        Assert.Equal(TenantId, tenant.OverrideTenantId);
        Assert.Equal(WorkspaceId, workspace.OverrideWorkspaceId);
        await primitive.Received(1).RunAsync(wf, OrchestrationPreset.Sequential, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Cross_Tenant_Job_Is_Dropped_Never_Run()
    {
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var foreign = new Workflow(Guid.NewGuid(), "other", Guid.NewGuid()); // 别的租户
        repo.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(foreign.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.NotFound, outcome);
        await primitive.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default);
    }

    [Fact]
    public async Task Execute_LeaseConflict_Maps_To_Duplicate_Not_Failure()
    {
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Workflow x is already running on another instance (lease held by other)"));

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        // 至少一次投递的重复常态：Duplicate（worker ack 跳过），不得进重试/dead-letter。
        Assert.Equal(QueuedRunOutcome.Duplicate, outcome);
    }

    [Fact]
    public async Task Execute_Running_Workflow_Duplicate_Delivery_Skipped_Via_Lease()
    {
        // F37 审查修复后：Running 不再本地预检跳过，统一交 F30 租约仲裁。
        // 活租约（另一实例在跑）→ RunAsync 抛冲突 → Duplicate（不重试不死信）。
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        wf.SetState(WorkflowState.Running);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Workflow x is already running on another instance (lease held by other)"));

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.Duplicate, outcome);
        await primitive.Received(1).RunAsync(wf, OrchestrationPreset.Sequential, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ExpiredLease_RunningWorkflow_Is_TakenOver_And_Resumed()
    {
        // 崩溃接管（验收 3）：worker 崩溃后 XAUTOCLAIM 重投，Running + 过期租约 → TryAcquireLease 成功续跑，
        // 不得被误判 Duplicate 吞掉。引擎侧租约语义由 F30 保证，此处断言消费者确实把作业交给引擎。
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        wf.SetState(WorkflowState.Running);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.Executed, outcome);
        await primitive.Received(1).RunAsync(wf, OrchestrationPreset.Sequential, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Terminal_Workflow_Duplicate_Delivery_Is_Never_ReRun()
    {
        // F1 回归守卫：人工运行作业入队前恒 Pending 落库；消费时已 Completed = 执行后的重复投递，
        // 必须 Duplicate 跳过（原 Reset 分支会把已完成工作流二次执行）。
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        wf.SetState(WorkflowState.Completed);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.Duplicate, outcome);
        await primitive.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default);
    }

    [Fact]
    public async Task Execute_Trigger_Job_Delegates_To_Trigger_Command_With_FromQueue()
    {
        var (handler, repo, _, _, _, mediator) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        var runResult = new TriggerRunResult(wf.Id, "wf", WorkflowState.Completed);
        // 对齐仓内既有可工作模式（WorkflowTriggersHandlersTests）：显式 Task.FromResult 配置返回值。
        mediator.Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TriggerRunResult?>(runResult));

        var job = BuildJob(wf.Id, triggerType: (int)TriggerType.Webhook, payloadJson: "{\"a\":1}");
        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(job), CancellationToken.None);

        await mediator.Received(1).Send(Arg.Any<TriggerWorkflowCommand>(), Arg.Any<CancellationToken>());
        Assert.Equal(QueuedRunOutcome.Executed, outcome);
        await mediator.Received(1).Send(
            Arg.Is<TriggerWorkflowCommand>(c => c.FromQueue && c.TriggerType == TriggerType.Webhook),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Failure_Maps_To_Failed_For_Worker_Retry()
    {
        var (handler, repo, primitive, _, _, _) = BuildExecutor();
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdAsync(wf.Id, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("model exploded"));

        var outcome = await handler.Handle(new ExecuteQueuedWorkflowCommand(BuildJob(wf.Id)), CancellationToken.None);

        Assert.Equal(QueuedRunOutcome.Failed, outcome);
    }

    // ── TriggerWorkflowCommandHandler 队列开关 ──

    private static (TriggerWorkflowCommandHandler Handler, IWorkflowRepository Repo, IOrchestrationPrimitive Primitive, IExecutionQueue Queue) BuildTrigger(
        bool queueEnabled,
        EnqueueResult enqueueResult = EnqueueResult.Enqueued)
    {
        var repo = Substitute.For<IWorkflowRepository>();
        var primitive = Substitute.For<IOrchestrationPrimitive>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var audit = Substitute.For<IAuditLogRepository>();
        var tenant = Substitute.For<ITenantContext>();
        var workspace = Substitute.For<IWorkspaceContext>();
        var directory = Substitute.For<IWorkspaceDirectory>();
        var queue = Substitute.For<IExecutionQueue>();
        queue.EnqueueAsync(Arg.Any<ExecutionJob>(), Arg.Any<CancellationToken>()).Returns(enqueueResult);
        var settings = Options.Create(new DurableExecutionSettings { QueueEnabled = queueEnabled });
        var logger = Substitute.For<ILogger<TriggerWorkflowCommandHandler>>();

        return (new TriggerWorkflowCommandHandler(
            repo, primitive, unitOfWork, audit, tenant, workspace, directory, queue, settings, logger), repo, primitive, queue);
    }

    [Fact]
    public async Task Trigger_In_QueueMode_Enqueues_And_Returns_Pending_Without_Running()
    {
        var (handler, repo, primitive, queue) = BuildTrigger(queueEnabled: true);
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdForTriggerAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);

        var result = await handler.Handle(
            new TriggerWorkflowCommand(wf.Id, TenantId, TriggerType.Webhook, "{\"x\":1}"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(WorkflowState.Pending, result!.State);
        await queue.Received(1).EnqueueAsync(
            Arg.Is<ExecutionJob>(j => j.WorkflowId == wf.Id && j.TriggerType == (int)TriggerType.Webhook),
            Arg.Any<CancellationToken>());
        await primitive.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default);
    }

    [Fact]
    public async Task Trigger_EnqueueRejected_Falls_Back_To_Direct_Run_Logged()
    {
        var (handler, repo, primitive, _) = BuildTrigger(queueEnabled: true, enqueueResult: EnqueueResult.RejectedBackendUnavailable);
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdForTriggerAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);

        var result = await handler.Handle(
            new TriggerWorkflowCommand(wf.Id, TenantId, TriggerType.Webhook, null), CancellationToken.None);

        // 匿名 Webhook 可用性优先：入队失败降级直跑（已记 warning，不静默）。
        Assert.NotNull(result);
        await primitive.Received(1).RunAsync(wf, OrchestrationPreset.Sequential, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trigger_FromQueue_Runs_Directly_Never_Enqueues()
    {
        var (handler, repo, primitive, queue) = BuildTrigger(queueEnabled: true);
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdForTriggerAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);

        var result = await handler.Handle(
            new TriggerWorkflowCommand(wf.Id, TenantId, TriggerType.Schedule, null, FromQueue: true), CancellationToken.None);

        Assert.NotNull(result);
        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trigger_QueueDisabled_Behaves_As_Before()
    {
        var (handler, repo, primitive, queue) = BuildTrigger(queueEnabled: false);
        var wf = new Workflow(Guid.NewGuid(), "wf", TenantId);
        repo.GetByIdForTriggerAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);
        primitive.RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>()).Returns(wf);

        await handler.Handle(new TriggerWorkflowCommand(wf.Id, TenantId, TriggerType.Webhook, null), CancellationToken.None);

        await queue.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
        await primitive.Received(1).RunAsync(wf, Arg.Any<OrchestrationPreset>(), Arg.Any<CancellationToken>());
    }
}
