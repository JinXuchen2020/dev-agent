using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using System.Collections.Generic;
using System.Linq;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

public sealed class OrchestrationPrimitiveTests
{
    private readonly IWorkflowRepository _repository = Substitute.For<IWorkflowRepository>();
    private readonly IRunningExecutionRepository _runningExecutionRepository = Substitute.For<IRunningExecutionRepository>();
    private readonly IExecutionLogRepository _executionLogRepository = Substitute.For<IExecutionLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDomainEventBus _eventBus = Substitute.For<IDomainEventBus>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ILogger<OrchestrationPrimitive> _logger = Substitute.For<ILogger<OrchestrationPrimitive>>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly ITokenCounter _tokenCounter = Substitute.For<ITokenCounter>();
    private readonly StateMachineSettings _settings = new()
    {
        MaxRetryAttempts = 2,
        StepTimeoutSeconds = 30,
        RetryDelayMs = 10,
        DefaultModelId = "test-model"
    };
    private readonly DurableExecutionSettings _durableSettings = new()
    {
        LeaseTtlMinutes = 5,
        CheckpointBatchSize = 5,
        CheckpointMaxAgeSeconds = 30
    };
    private readonly OrchestrationPrimitive _primitive;

    public OrchestrationPrimitiveTests()
    {
        _primitive = new OrchestrationPrimitive(
            _repository, _runningExecutionRepository, _executionLogRepository, _unitOfWork, _eventBus, _serviceProvider,
            Options.Create(_settings), Options.Create(_durableSettings), _logger, _vectorStore, _tokenCounter);
    }

    private static Workflow CreateWorkflow(string name = "test-workflow", int stepCount = 3)
    {
        var workflow = new Workflow(Guid.NewGuid(), name, Guid.NewGuid());
        for (var i = 0; i < stepCount; i++)
        {
            workflow.AddStep(new WorkflowStep(Guid.NewGuid(), i, $"step-{i}"));
        }
        return workflow;
    }

    private static IStepExecutor CreateStepExecutor(Func<IWorkflowExecutable, WorkflowContext, StepExecutionResult> execute)
    {
        var executor = Substitute.For<IStepExecutor>();
        executor.StepType.Returns("*");
        executor.ExecuteAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => execute(
                call.ArgAt<IWorkflowExecutable>(0),
                call.ArgAt<WorkflowContext>(1)));
        return executor;
    }

    private static IStepExecutor CreateStepExecutor(
        Func<IWorkflowExecutable, WorkflowContext, CancellationToken, Task<StepExecutionResult>> execute)
    {
        var executor = Substitute.For<IStepExecutor>();
        executor.StepType.Returns("*");
        executor.ExecuteAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => execute(
                call.ArgAt<IWorkflowExecutable>(0),
                call.ArgAt<WorkflowContext>(1),
                call.ArgAt<CancellationToken>(2)));
        return executor;
    }

    private void SetupExecutor(IStepExecutor executor)
    {
        var executors = new[] { executor };

        // ResolveExecutor uses _serviceProvider.GetServices<IStepExecutor>()
        // which calls GetRequiredService<IEnumerable<IStepExecutor>>()
        _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>))
            .Returns(executors);

        // Sequential path creates scope; mock the scoped provider too for completeness
        var scopedProvider = CreateScopedProvider(executors, null, null);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scopedProvider.scope);
        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
    }

    private void SetupNegotiation(
        IStepExecutor executor,
        ISelectionStrategy? selectionStrategy,
        ITerminationCondition? terminationCondition)
    {
        var executors = new[] { executor };

        _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>))
            .Returns(executors);

        var (scopedProvider, scope) = CreateScopedProvider(executors, selectionStrategy, terminationCondition);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
    }

    private static (IServiceProvider provider, IServiceScope scope) CreateScopedProvider(
        IStepExecutor[] executors,
        ISelectionStrategy? selectionStrategy,
        ITerminationCondition? terminationCondition)
    {
        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(IEnumerable<IStepExecutor>))
            .Returns(executors);
        if (selectionStrategy != null)
            scopedProvider.GetService(typeof(ISelectionStrategy)).Returns(selectionStrategy);
        if (terminationCondition != null)
            scopedProvider.GetService(typeof(ITerminationCondition)).Returns(terminationCondition);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopedProvider);
        return (scopedProvider, scope);
    }

    // ──────────────────────────────────────────────
    // Happy path: Sequential preset completes all steps
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Sequential_CompletesAllSteps()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}",
                $"{{\"step\":\"{step.Name}\"}}"));
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.All(result.Steps, s => Assert.Equal(WorkflowState.Completed, s.State));
    }

    // ──────────────────────────────────────────────
    // Regression: an already-tracked (existing) workflow must NOT be re-Added.
    // RunAsync used to call _repository.Add unconditionally, which re-inserted the
    // existing row and threw DbUpdateException (UNIQUE constraint failed: Workflows.Id)
    // → HTTP 500 on every re-run of an existing workflow.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ExistingTrackedWorkflow_DoesNotReAdd()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        // Simulate the workflow being loaded (tracked) from the repository, as the
        // RunExistingWorkflow / Resume / Retry paths do via FindAsync.
        _unitOfWork.GetTrackedAggregates().Returns(new List<IAggregateRoot> { workflow });
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}", "{}"));
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        // The workflow was already tracked, so RunAsync must NOT call Add (which would
        // re-insert the existing primary key and violate the unique constraint).
        _repository.DidNotReceive().Add(Arg.Any<Workflow>());
    }

    // ──────────────────────────────────────────────
    // Retry: step fails then succeeds on retry
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Sequential_RetryThenSucceeds()
    {
        var workflow = CreateWorkflow(stepCount: 1);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var attempt = 0;
        var executor = CreateStepExecutor((step, ctx) =>
        {
            attempt++;
            return attempt < 2
                ? StepExecutionResult.RetryableFailure("transient error")
                : StepExecutionResult.Success("final-output");
        });
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.Equal(2, attempt);
    }

    // ──────────────────────────────────────────────
    // Rollback: all retries exhausted
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Sequential_RollsBackAfterRetriesExhausted()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.RetryableFailure("always fails"));
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.RolledBack, result.CurrentState);
    }

    // ──────────────────────────────────────────────
    // Retry semantics: MaxRetryAttempts = TOTAL attempts (first + retries).
    // Locks the off-by-one fix: with MaxRetryAttempts=2 the executor must be
    // invoked exactly 2 times, never 3.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Sequential_InvokesExecutorExactlyMaxRetryAttemptsTimes()
    {
        var workflow = CreateWorkflow(stepCount: 1);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var callCount = 0;
        var executor = CreateStepExecutor((step, ctx) =>
        {
            callCount++;
            return StepExecutionResult.RetryableFailure("always fails");
        });
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.RolledBack, result.CurrentState);
        // MaxRetryAttempts = 2 → exactly 2 total attempts (no off-by-one).
        Assert.Equal(2, callCount);
    }

    // ──────────────────────────────────────────────
    // Pause and Resume
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PauseAndResume_ContinuesExecution()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        workflow.SetState(WorkflowState.Running);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}"));
        SetupExecutor(executor);

        await _primitive.PauseAsync(workflow.Id);
        Assert.Equal(WorkflowState.Paused, workflow.CurrentState);

        var resumed = await _primitive.ResumeAsync(workflow.Id);
        Assert.Equal(WorkflowState.Completed, resumed.CurrentState);
    }

    // ──────────────────────────────────────────────
    // Retry specific step
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RetryStepAsync_ResetsAndReExecutesStep()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        workflow.SetState(WorkflowState.Running);
        workflow.Steps[0].SetState(WorkflowState.Failed);
        workflow.Steps[0].SetError("failed once");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"retried-{step.Name}"));
        SetupExecutor(executor);

        await _primitive.RetryStepAsync(workflow.Id, 0);

        Assert.Equal(WorkflowState.Completed, workflow.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Steps[0].State);
    }

    // ──────────────────────────────────────────────
    // Rollback to specific step
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RollbackToAsync_ResetsTargetAndSubsequentSteps()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        workflow.Steps[0].SetResult("done");
        workflow.Steps[1].SetResult("done");
        workflow.Steps[2].SetState(WorkflowState.Failed);
        workflow.Steps[2].SetError("error");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        await _primitive.RollbackToAsync(workflow.Id, targetStepOrder: 1);

        Assert.Equal(WorkflowState.RolledBack, workflow.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Steps[0].State);
        Assert.Equal(WorkflowState.Pending, workflow.Steps[1].State);
        Assert.Equal(WorkflowState.Pending, workflow.Steps[2].State);
    }

    // ──────────────────────────────────────────────
    // GetStateAsync returns snapshot
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetStateAsync_ReturnsSnapshot()
    {
        var workflow = CreateWorkflow();
        workflow.Steps[0].SetResult("done");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var snapshot = await _primitive.GetStateAsync(workflow.Id);

        Assert.Equal(workflow.Id, snapshot.WorkflowId);
        Assert.Equal(workflow.CurrentState, snapshot.CurrentState);
        Assert.Single(snapshot.Steps, s => s.State == WorkflowState.Completed);
    }

    // ──────────────────────────────────────────────
    // Null guard
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ThrowsOnNullWorkflow()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _primitive.RunAsync(null!, OrchestrationPreset.Sequential));
    }

    // ──────────────────────────────────────────────
    // Cancellation during step execution
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsOperationCanceled()
    {
        var workflow = CreateWorkflow(stepCount: 1);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
        {
            Assert.NotNull(ctx);
            throw new OperationCanceledException();
        });
        SetupExecutor(executor);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _primitive.RunAsync(workflow, OrchestrationPreset.Sequential, cts.Token));
    }

    // ──────────────────────────────────────────────
    // Sequential: executes steps in order
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SequentialPreset_RunsInOrder()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executionOrder = new List<int>();
        var executor = CreateStepExecutor((step, ctx) =>
        {
            executionOrder.Add(step.Order);
            return StepExecutionResult.Success($"output-{step.Name}");
        });
        SetupExecutor(executor);

        await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal([0, 1, 2], executionOrder);
    }

    // ──────────────────────────────────────────────
    // Negotiation preset
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Negotiation_TerminatesWhenConditionMet()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}"));
        var termination = Substitute.For<ITerminationCondition>();
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var selection = Substitute.For<ISelectionStrategy>();
        SetupNegotiation(executor, selection, termination);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Negotiation);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        // Should have called termination but NOT selection (termination triggered first)
        await termination.Received(1).ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>());
        await selection.DidNotReceiveWithAnyArgs().SelectNextAsync(default!, default!);
    }

    [Fact]
    public async Task RunAsync_Negotiation_CompletesWhenNoEligibleStep()
    {
        var workflow = CreateWorkflow(stepCount: 3);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}"));
        var termination = Substitute.For<ITerminationCondition>();
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        var selection = Substitute.For<ISelectionStrategy>();
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkflowStep?>(null));
        SetupNegotiation(executor, selection, termination);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Negotiation);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
    }

    [Fact]
    public async Task RunAsync_Negotiation_ExecutesSelectedStep()
    {
        var workflow = CreateWorkflow(stepCount: 1);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}"));
        var termination = Substitute.For<ITerminationCondition>();
        bool firstCheck = true;
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (firstCheck) { firstCheck = false; return Task.FromResult(false); }
                return Task.FromResult(true); // terminate after first step
            });
        var selection = Substitute.For<ISelectionStrategy>();
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns(workflow.Steps[0]); // Return the step
        SetupNegotiation(executor, selection, termination);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Negotiation);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Steps[0].State);
    }

    [Fact]
    public async Task RunAsync_Negotiation_ContinuesAfterFailedRetry()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        int callCount = 0;
        var executor = CreateStepExecutor((step, ctx) =>
        {
            callCount++;
            return callCount <= 1
                ? StepExecutionResult.RetryableFailure("transient error")
                : StepExecutionResult.Success($"output-{step.Name}");
        });
        var termination = Substitute.For<ITerminationCondition>();
        bool firstCall = true;
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (firstCall) { firstCall = false; return Task.FromResult(false); }
                return Task.FromResult(true); // terminate after first step
            });
        var selection = Substitute.For<ISelectionStrategy>();
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns(workflow.Steps[0]);
        SetupNegotiation(executor, selection, termination);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Negotiation);
        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        // FailedRetry should keep the state as completed
        Assert.True(callCount >= 2, $"Expected at least 2 calls, got {callCount}");
    }

    [Fact]
    public async Task RunAsync_Negotiation_RollsBackOnFatalFailure()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.FatalFailure("unrecoverable"));
        var termination = Substitute.For<ITerminationCondition>();
        termination.ShouldTerminateAsync(Arg.Any<WorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        var selection = Substitute.For<ISelectionStrategy>();
        selection.SelectNextAsync(Arg.Any<WorkflowContext>(), Arg.Any<IReadOnlyList<WorkflowStep>>(), Arg.Any<CancellationToken>())
            .Returns(workflow.Steps[0]);
        SetupNegotiation(executor, selection, termination);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Negotiation);

        Assert.Equal(WorkflowState.RolledBack, result.CurrentState);
    }

    // ──────────────────────────────────────────────
    // Crash recovery / Resume continuity (Blueprint C.7): a workflow that
    // partially completed (e.g. host crashed mid-run) must RESUME from the
    // last completed step — already-completed steps must NOT be re-executed.
    // This is the test the 07-20 review incorrectly waived as "needs Docker".
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_Sequential_SkipsAlreadyCompletedSteps_OnResume()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        // Simulate a workflow that ran partway then restarted: step-0 already
        // succeeded, step-1 still pending.
        workflow.SetState(WorkflowState.Running);
        workflow.Steps[0].SetResult("already-completed");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executed = new List<string>();
        var executor = CreateStepExecutor((step, ctx) =>
        {
            executed.Add(step.Name);
            return StepExecutionResult.Success($"output-{step.Name}");
        });
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        // Only the pending step must run; the completed step must NOT re-execute.
        Assert.Single(executed);
        Assert.DoesNotContain("step-0", executed);
        Assert.Contains("step-1", executed);
    }

    // ──────────────────────────────────────────────
    // Pause (F30): PauseAsync marks workflow as Paused and updates RunningExecution.
    // Unlike the old CTS-based mechanism, the step continues to completion;
    // the scheduler will not resume a Paused workflow.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PauseAsync_MarksWorkflowPaused_UpdatesRunningExecution()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        workflow.SetState(WorkflowState.Running);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            StepExecutionResult.Success($"output-{step.Name}"));
        SetupExecutor(executor);

        // Pause should mark workflow as Paused
        await _primitive.PauseAsync(workflow.Id);

        Assert.Equal(WorkflowState.Paused, workflow.CurrentState);
    }

    // ──────────────────────────────────────────────
    // F20：编排器 DAG 引擎 — 条件分支跳过 / 汇合安全 / 循环内联
    // ──────────────────────────────────────────────

    private static Workflow CreateDag(
        IReadOnlyList<(Guid TempId, StepType Type, string Name, double X, double Y, string? Config, Guid? AgentId)> nodes,
        IReadOnlyList<(Guid TempId, Guid SourceTempId, Guid TargetTempId, string? Label)> edges)
    {
        var workflow = new Workflow(Guid.NewGuid(), "f20-dag", Guid.NewGuid());
        workflow.ReplaceGraph(nodes, edges);
        return workflow;
    }

    private void SetupExecutors(params IStepExecutor[] executors)
    {
        _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>)).Returns(executors);
        var scoped = CreateScopedProvider(executors, null, null);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scoped.scope);
        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
    }

    /// <summary>
    /// 构建一个带 Condition 分支的 DAG（Start → Cond →{true:True, false:False}→ End）。
    /// 返回的 executor 对 Condition 节点回传 <paramref name="branch"/>，其余节点回传 Success。
    /// </summary>
    private (Workflow workflow, IStepExecutor executor) BuildConditionDag(string branch)
    {
        var s = Guid.NewGuid(); var c = Guid.NewGuid(); var t = Guid.NewGuid();
        var f = Guid.NewGuid(); var e = Guid.NewGuid();
        var nodes = new List<(Guid, StepType, string, double, double, string?, Guid?)>
        {
            (s, StepType.Start, "Start", 0, 0, null, null),
            (c, StepType.Condition, "Cond", 0, 0, "{\"expression\":\"1 === 1\"}", null),
            (t, StepType.LLM, "TrueBranch", 0, 0, null, null),
            (f, StepType.LLM, "FalseBranch", 0, 0, null, null),
            (e, StepType.End, "End", 0, 0, null, null),
        };
        var edges = new List<(Guid, Guid, Guid, string?)>
        {
            (Guid.NewGuid(), s, c, null),
            (Guid.NewGuid(), c, t, "true"),
            (Guid.NewGuid(), c, f, "false"),
            (Guid.NewGuid(), t, e, null),
            (Guid.NewGuid(), f, e, null),
        };
        var workflow = CreateDag(nodes, edges);
        var executor = CreateStepExecutor((step, ctx) =>
            step.Type == StepType.Condition
                ? StepExecutionResult.Success(branch)
                : StepExecutionResult.Success($"output-{step.Name}"));
        return (workflow, executor);
    }

    [Fact]
    public async Task RunAsync_Sequential_ConditionTrue_ExecutesSelectedBranch_SkipsOther()
    {
        var (workflow, executor) = BuildConditionDag("true");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);
        SetupExecutors(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Nodes.Single(n => n.Name == "TrueBranch").State);
        // 非选中分支（false）必须被跳过且保持 Pending，工作流仍应完成。
        Assert.Equal(WorkflowState.Pending, workflow.Nodes.Single(n => n.Name == "FalseBranch").State);
    }

    [Fact]
    public async Task RunAsync_Sequential_ConditionFalse_ExecutesSelectedBranch_SkipsOther()
    {
        var (workflow, executor) = BuildConditionDag("false");
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);
        SetupExecutors(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Nodes.Single(n => n.Name == "FalseBranch").State);
        Assert.Equal(WorkflowState.Pending, workflow.Nodes.Single(n => n.Name == "TrueBranch").State);
    }

    // ── 汇合安全：选中分支与跳过分支在 join 节点重新汇合，join 节点不得被误跳 ──

    [Fact]
    public async Task RunAsync_Sequential_ConditionBranch_JoinNodeStillExecutes()
    {
        var s = Guid.NewGuid(); var c = Guid.NewGuid(); var t = Guid.NewGuid();
        var f = Guid.NewGuid(); var j = Guid.NewGuid(); var e = Guid.NewGuid();
        var nodes = new List<(Guid, StepType, string, double, double, string?, Guid?)>
        {
            (s, StepType.Start, "Start", 0, 0, null, null),
            (c, StepType.Condition, "Cond", 0, 0, "{\"expression\":\"1 === 1\"}", null),
            (t, StepType.LLM, "TrueBranch", 0, 0, null, null),
            (f, StepType.LLM, "FalseBranch", 0, 0, null, null),
            (j, StepType.LLM, "Join", 0, 0, null, null),
            (e, StepType.End, "End", 0, 0, null, null),
        };
        var edges = new List<(Guid, Guid, Guid, string?)>
        {
            (Guid.NewGuid(), s, c, null),
            (Guid.NewGuid(), c, t, "true"),
            (Guid.NewGuid(), c, f, "false"),
            (Guid.NewGuid(), t, j, null),
            (Guid.NewGuid(), f, j, null),
            (Guid.NewGuid(), j, e, null),
        };
        var workflow = CreateDag(nodes, edges);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor((step, ctx) =>
            step.Type == StepType.Condition
                ? StepExecutionResult.Success("true")
                : StepExecutionResult.Success($"output-{step.Name}"));
        SetupExecutors(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.Equal(WorkflowState.Completed, workflow.Nodes.Single(n => n.Name == "TrueBranch").State);
        Assert.Equal(WorkflowState.Pending, workflow.Nodes.Single(n => n.Name == "FalseBranch").State);
        // 汇合点：虽有一条入边来自被跳过的 false 分支，但它也由 true 分支可达，故必须执行。
        Assert.Equal(WorkflowState.Completed, workflow.Nodes.Single(n => n.Name == "Join").State);
    }

    // ── 循环内联：Loop 节点的 body 子图对每项迭代一次，itemVariable 注入共享 Blackboard ──

    [Fact]
    public async Task RunAsync_Sequential_Loop_ExecutesBodyPerItem_WithItemVariable()
    {
        var s = Guid.NewGuid(); var loop = Guid.NewGuid(); var body = Guid.NewGuid(); var e = Guid.NewGuid();
        var nodes = new List<(Guid, StepType, string, double, double, string?, Guid?)>
        {
            (s, StepType.Start, "Start", 0, 0, null, null),
            (loop, StepType.Loop, "Loop", 0, 0,
                "{\"itemsSource\":\"[\\\"a\\\",\\\"b\\\",\\\"c\\\"]\",\"itemVariable\":\"item\",\"bodyNodeNames\":[\"Body\"]}", null),
            (body, StepType.LLM, "Body", 0, 0, null, null),
            (e, StepType.End, "End", 0, 0, null, null),
        };
        var edges = new List<(Guid, Guid, Guid, string?)>
        {
            (Guid.NewGuid(), s, loop, null),
            (Guid.NewGuid(), loop, body, null),
            (Guid.NewGuid(), body, e, null),
        };
        var workflow = CreateDag(nodes, edges);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var capturedItems = new List<string>();
        var executor = CreateStepExecutor((step, ctx) =>
        {
            if (step.Type == StepType.Loop)
                return StepExecutionResult.Success("loop");
            // Body 节点：记录注入共享 Blackboard 的 item 变量当前值。
            capturedItems.Add(ctx.Blackboard.Get("item") ?? "<null>");
            return StepExecutionResult.Success($"body-{step.Name}");
        });
        SetupExecutors(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        // Body 必须对每个 item 迭代一次（3 项），且主线性遍历因 loopBodyIds 跳过它，故不会重复。
        Assert.Equal(new[] { "a", "b", "c" }, capturedItems);
        // Loop 节点自身应产生完成结果（含迭代计数）。
        var loopNode = workflow.Nodes.Single(n => n.Name == "Loop");
        Assert.Equal(WorkflowState.Completed, loopNode.State);
        Assert.Contains("3 items", loopNode.Result ?? "");
    }
}
