using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AgentPlatform.Infrastructure.Workflows;

internal sealed class WorkflowStateMachineEngine : IStateMachineEngine, IWorkflowEngine
{
    private readonly IEnumerable<IStepExecutor> _executors;
    private readonly StateMachineSettings _settings;
    private readonly ILogger<WorkflowStateMachineEngine> _logger;
    private readonly IDomainEventBus _eventBus;
    private readonly ConcurrentDictionary<Guid, WorkflowExecutionState> _activeWorkflows = new();

    public WorkflowStateMachineEngine(
        IEnumerable<IStepExecutor> executors,
        IOptions<StateMachineSettings> settings,
        ILogger<WorkflowStateMachineEngine> logger,
        IDomainEventBus eventBus)
    {
        _executors = executors;
        _settings = settings.Value;
        _logger = logger;
        _eventBus = eventBus;
    }

    async Task<WorkflowState> IStateMachineEngine.StartAsync(Workflow workflow, CancellationToken ct)
    {
        var execState = _activeWorkflows.GetOrAdd(workflow.Id, _ => new WorkflowExecutionState());
        lock (execState.Lock)
        {
            if (workflow.CurrentState != WorkflowState.Pending)
                throw new InvalidOperationException($"Workflow {workflow.Id} is not in Pending state (current: {workflow.CurrentState})");
            workflow.SetState(WorkflowState.Running);
            execState.Workflow = workflow;
        }

        // Publish WorkflowStarted immediately so execution log is created before step events
        await _eventBus.PublishAsync(
            new WorkflowStarted(workflow.Id, workflow.Name, workflow.TenantId), ct);

        try
        {
        // Order steps once to avoid O(n²) complexity
        var orderedSteps = workflow.Steps.OrderBy(s => s.Order).ToList();

        foreach (var step in orderedSteps)
        {
            ct.ThrowIfCancellationRequested();

            var result = await ExecuteStepWithRetryAsync(workflow, step, ct);

            if (result.IsSuccess)
            {
                var isLastStep = step.Order == orderedSteps.Last().Order;
                if (isLastStep)
                {
                    workflow.Complete();
                    await _eventBus.PublishAsync(
                        new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
                }
                else
                {
                    workflow.SetState(WorkflowState.Running);
                }
                continue;
            }

            var branched = await TryBranchAsync(workflow, step, ct);
            if (branched)
            {
                var isLastStep = step.Order == orderedSteps.Last().Order;
                if (isLastStep)
                {
                    workflow.Complete();
                    await _eventBus.PublishAsync(
                        new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
                }
                else
                {
                    workflow.SetState(WorkflowState.Running);
                }
                continue;
            }

            // Publish StepFailed only after branching is exhausted — prevents double-publishing
                await _eventBus.PublishAsync(
                    new StepFailed(workflow.Id, step.Id, step.StepName, step.Order, step.ErrorDetail ?? "All retry attempts exhausted", result.Duration), ct);
                await RollbackCompletedStepsAsync(workflow, step.StepName, step.ErrorDetail ?? "All retry attempts exhausted", ct);
                return WorkflowState.RolledBack;
            }

            return workflow.CurrentState;
        }
        finally
        {
            _activeWorkflows.TryRemove(workflow.Id, out _);
        }
    }

    async Task IWorkflowEngine.StartAsync(Workflow workflow, CancellationToken ct)
    {
        await ((IStateMachineEngine)this).StartAsync(workflow, ct);
    }

    Task IWorkflowEngine.PauseAsync(Guid workflowId, CancellationToken ct)
    {
        return ((IStateMachineEngine)this).PauseAsync(workflowId, ct);
    }

    async Task IWorkflowEngine.ResumeAsync(Guid workflowId, CancellationToken ct)
    {
        await ((IStateMachineEngine)this).ResumeAsync(workflowId, ct);
    }

    Task IWorkflowEngine.RetryAsync(Guid workflowId, int stepOrder, CancellationToken ct)
    {
        _logger.LogInformation("Retry requested for workflow {WorkflowId} step {StepOrder}", workflowId, stepOrder);
        return Task.CompletedTask;
    }

    Task IWorkflowEngine.RollbackAsync(Guid workflowId, int targetStepOrder, CancellationToken ct)
    {
        _logger.LogInformation("Rollback requested for workflow {WorkflowId} to step {TargetStepOrder}", workflowId, targetStepOrder);
        return Task.CompletedTask;
    }

    Task<WorkflowStateSnapshot> IWorkflowEngine.GetStateAsync(Guid workflowId, CancellationToken ct)
    {
        _activeWorkflows.TryGetValue(workflowId, out var execState);
        if (execState?.Workflow == null)
        {
            return Task.FromResult(new WorkflowStateSnapshot(workflowId, WorkflowState.Pending, 0, []));
        }

        lock (execState.Lock)
        {
            var stepSnapshots = execState.Workflow.Steps.Select(s => new StepSnapshot(
                s.Id, s.Order, s.StepName, s.State, s.Result, s.ErrorDetail)).ToArray();
            return Task.FromResult(new WorkflowStateSnapshot(workflowId, execState.Workflow.CurrentState, 0, stepSnapshots));
        }
    }

    private async Task<StepExecutionResult> ExecuteStepWithRetryAsync(Workflow workflow, WorkflowStep step, CancellationToken ct)
    {
        var retryCount = 0;
        StepExecutionResult? lastResult = null;

        while (retryCount <= _settings.MaxRetryAttempts)
        {
            ct.ThrowIfCancellationRequested();

            step.SetState(WorkflowState.Running);
            _logger.LogInformation("Executing step {StepName} (attempt {Attempt}/{MaxRetry})",
                step.StepName, retryCount + 1, _settings.MaxRetryAttempts + 1);

            var executor = ResolveExecutor(step);
            if (executor == null)
            {
                step.SetError("No executor found for step: " + step.StepName);
                return new StepExecutionResult(false, null, step.ErrorDetail ?? "No executor found");
            }

            var startTime = DateTime.UtcNow;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.StepTimeoutSeconds));
                lastResult = await executor.ExecuteAsync(step, workflow, timeoutCts.Token);

                var duration = DateTime.UtcNow - startTime;

                if (lastResult.IsSuccess)
                {
                    step.SetState(WorkflowState.Completed);
                    await _eventBus.PublishAsync(
                        new StepCompleted(workflow.Id, step.Id, step.StepName, step.Order, lastResult.Output, duration), ct);
                    return lastResult;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var duration = DateTime.UtcNow - startTime;
                lastResult = new StepExecutionResult(false, null, $"Step timed out after {_settings.StepTimeoutSeconds}s", duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                _logger.LogWarning(ex, "Step {StepName} failed on attempt {Attempt}", step.StepName, retryCount + 1);
                lastResult = new StepExecutionResult(false, null, ex.Message, duration);
            }

            retryCount++;
            step.SetState(WorkflowState.Failed);
            step.SetError(lastResult.ErrorMessage ?? "Unknown error");

            if (retryCount <= _settings.MaxRetryAttempts)
            {
                _logger.LogInformation("Retrying step {StepName} in 1s (attempt {Attempt}/{MaxRetry})",
                    step.StepName, retryCount, _settings.MaxRetryAttempts);
                await Task.Delay(1000, ct);
            }
        }

        // All retry attempts exhausted
        var finalError = lastResult?.ErrorMessage ?? "All retry attempts exhausted";
        step.SetError(finalError);
        return lastResult ?? new StepExecutionResult(false, null, finalError);
    }

    private async Task<bool> TryBranchAsync(Workflow workflow, WorkflowStep failedStep, CancellationToken ct)
    {
        var branchExecutors = _executors.Where(e => e.StepType == "branch").ToList();
        if (branchExecutors.Count == 0)
            return false;

        foreach (var branchExecutor in branchExecutors)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var result = await branchExecutor.ExecuteAsync(failedStep, workflow, ct);
                var duration = DateTime.UtcNow - startTime;
                if (result.IsSuccess)
                {
                    failedStep.SetState(WorkflowState.Completed);
                    await _eventBus.PublishAsync(
                        new StepCompleted(workflow.Id, failedStep.Id, failedStep.StepName, failedStep.Order, result.Output, duration), ct);
                    _logger.LogInformation("Branch step executed successfully for step {StepName}", failedStep.StepName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Branch execution failed for step {StepName}", failedStep.StepName);
            }
        }

        return false;
    }

    private async Task RollbackCompletedStepsAsync(Workflow workflow, string failedStepName, string errorDetail, CancellationToken ct)
    {
        var execState = _activeWorkflows.GetOrAdd(workflow.Id, _ => new WorkflowExecutionState());
        lock (execState.Lock)
        {
            foreach (var step in workflow.Steps.Where(s => s.State == WorkflowState.Completed))
            {
                step.SetState(WorkflowState.Pending);
                _logger.LogInformation("Rolled back step {StepName}", step.StepName);
            }

            workflow.Rollback();
        }

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name, failedStepName, errorDetail, workflow.TenantId), ct);
    }

    private IStepExecutor? ResolveExecutor(WorkflowStep step)
    {
        return _executors.FirstOrDefault(e => e.StepType == step.StepName)
            ?? _executors.FirstOrDefault(e => e.StepType == "*")
            ?? _executors.FirstOrDefault();
    }

    Task IStateMachineEngine.PauseAsync(Guid workflowId, CancellationToken ct)
    {
        _logger.LogInformation("Pause requested for workflow {WorkflowId}", workflowId);
        return Task.CompletedTask;
    }

    Task<WorkflowState> IStateMachineEngine.ResumeAsync(Guid workflowId, CancellationToken ct)
    {
        _logger.LogInformation("Resume requested for workflow {WorkflowId}", workflowId);
        return Task.FromResult(WorkflowState.Running);
    }

    Task<WorkflowState> IStateMachineEngine.GetStatusAsync(Guid workflowId, CancellationToken ct)
    {
        if (_activeWorkflows.TryGetValue(workflowId, out var execState))
        {
            lock (execState.Lock)
            {
                return Task.FromResult(execState.Workflow?.CurrentState ?? WorkflowState.Pending);
            }
        }

        return Task.FromResult(WorkflowState.Pending);
    }

    private sealed class WorkflowExecutionState
    {
        public readonly object Lock = new();
        public Workflow? Workflow;
    }
}
