using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDomainEventBus _eventBus = Substitute.For<IDomainEventBus>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly ILogger<OrchestrationPrimitive> _logger = Substitute.For<ILogger<OrchestrationPrimitive>>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly StateMachineSettings _settings = new()
    {
        MaxRetryAttempts = 2,
        StepTimeoutSeconds = 30,
        RetryDelayMs = 10,
        DefaultModelId = "test-model"
    };
    private readonly OrchestrationPrimitive _primitive;

    public OrchestrationPrimitiveTests()
    {
        _primitive = new OrchestrationPrimitive(
            _repository, _unitOfWork, _eventBus, _serviceProvider,
            Options.Create(_settings), _logger, _vectorStore);
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

    private static IStepExecutor CreateStepExecutor(Func<WorkflowStep, WorkflowContext, StepExecutionResult> execute)
    {
        var executor = Substitute.For<IStepExecutor>();
        executor.StepType.Returns("*");
        executor.ExecuteAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => execute(
                call.ArgAt<WorkflowStep>(0),
                call.ArgAt<WorkflowContext>(1)));
        return executor;
    }

    private static IStepExecutor CreateStepExecutor(
        Func<WorkflowStep, WorkflowContext, CancellationToken, Task<StepExecutionResult>> execute)
    {
        var executor = Substitute.For<IStepExecutor>();
        executor.StepType.Returns("*");
        executor.ExecuteAsync(default!, default!, default)
            .ReturnsForAnyArgs(call => execute(
                call.ArgAt<WorkflowStep>(0),
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
            StepExecutionResult.Success($"output-{step.StepName}",
                $"{{\"step\":\"{step.StepName}\"}}"));
        SetupExecutor(executor);

        var result = await _primitive.RunAsync(workflow, OrchestrationPreset.Sequential);

        Assert.Equal(WorkflowState.Completed, result.CurrentState);
        Assert.All(result.Steps, s => Assert.Equal(WorkflowState.Completed, s.State));
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
            StepExecutionResult.Success($"output-{step.StepName}"));
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
            StepExecutionResult.Success($"retried-{step.StepName}"));
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
            return StepExecutionResult.Success($"output-{step.StepName}");
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
            StepExecutionResult.Success($"output-{step.StepName}"));
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
            StepExecutionResult.Success($"output-{step.StepName}"));
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
            StepExecutionResult.Success($"output-{step.StepName}"));
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
                : StepExecutionResult.Success($"output-{step.StepName}");
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
            executed.Add(step.StepName);
            return StepExecutionResult.Success($"output-{step.StepName}");
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
    // Pause mid-execution (Blueprint C.7): PauseAsync must interrupt an
    // in-flight run and leave the workflow Paused + resumable.
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PauseAsync_InterruptsInFlightRun_LeavesWorkflowPaused()
    {
        var workflow = CreateWorkflow(stepCount: 2);
        workflow.SetState(WorkflowState.Running);
        _repository.GetByIdAsync(workflow.Id, default).Returns(workflow);

        var executor = CreateStepExecutor(async (step, ctx, ct) =>
        {
            if (step.Order == 0)
            {
                // Simulate a long-running first step; issue Pause while it is in flight.
                _ = _primitive.PauseAsync(workflow.Id);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { throw; }
            }
            return StepExecutionResult.Success($"output-{step.StepName}");
        });
        SetupExecutor(executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _primitive.RunAsync(workflow, OrchestrationPreset.Sequential));

        Assert.Equal(WorkflowState.Paused, workflow.CurrentState);
    }
}
