using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// The single orchestration primitive for the platform (Blueprint C.2).
/// Routes to the appropriate preset (sequential / negotiation) and handles
/// per-step persistence, domain event publishing, and lifecycle operations.
///
/// Replaces the old dual-track: WorkflowStateMachineEngine + AutoGenAgentOrchestrator.
/// </summary>
internal sealed class OrchestrationPrimitive : IOrchestrationPrimitive
{
    private readonly IWorkflowRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrchestrationPrimitive> _logger;
    private readonly StateMachineSettings _settings;

    public OrchestrationPrimitive(
        IWorkflowRepository repository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        IOptions<StateMachineSettings> settings,
        ILogger<OrchestrationPrimitive> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<Workflow> RunAsync(Workflow workflow, OrchestrationPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        if (workflow.CurrentState != WorkflowState.Pending && workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException(
                $"Workflow {workflow.Id} cannot be started (state: {workflow.CurrentState})");

        // Ensure the workflow is persisted before starting
        workflow.SetState(WorkflowState.Running);
        _repository.Add(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        // Publish WorkflowStarted
        await _eventBus.PublishAsync(
            new WorkflowStarted(workflow.Id, workflow.Name, workflow.TenantId), ct);

        try
        {
            switch (preset)
            {
                case OrchestrationPreset.Sequential:
                    await RunSequentialAsync(workflow, ct);
                    break;
                case OrchestrationPreset.Negotiation:
                    await RunNegotiationAsync(workflow, ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout or external cancellation — leave workflow Running for resume
            _logger.LogWarning("Workflow {WorkflowId} execution timed out or was cancelled", workflow.Id);
            workflow.SetState(WorkflowState.Paused);
            _repository.Update(workflow);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }

        return workflow;
    }

    public async Task PauseAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is not running (state: {workflow.CurrentState})");

        workflow.SetState(WorkflowState.Paused);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Workflow {WorkflowId} paused", workflowId);
    }

    public async Task<Workflow> ResumeAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Paused)
            throw new InvalidOperationException($"Workflow {workflowId} is not paused (state: {workflow.CurrentState})");

        // Reload preset from stored step data
        var preset = DetectPreset(workflow);
        workflow.SetState(WorkflowState.Running);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await RunAsync(workflow, preset, ct);
        return workflow;
    }

    public async Task RetryStepAsync(Guid workflowId, int stepOrder, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var failedStep = workflow.Steps.FirstOrDefault(s => s.Order == stepOrder)
            ?? throw new InvalidOperationException($"Step order {stepOrder} not found in workflow {workflowId}");

        // Reset the step to Pending (clear prior error state)
        failedStep.SetState(WorkflowState.Pending);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        // Re-run from the reset step
        workflow.SetState(WorkflowState.Running);
        await RunAsync(workflow, DetectPreset(workflow), ct);
    }

    public async Task RollbackToAsync(Guid workflowId, int targetStepOrder, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);

        // Precise rollback: reset target and all subsequent steps to Pending (Blueprint C.6)
        foreach (var step in workflow.Steps.Where(s => s.Order >= targetStepOrder))
        {
            step.SetState(WorkflowState.Pending);
        }

        workflow.SetState(WorkflowState.RolledBack);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name,
                $"Rolled back to step {targetStepOrder}",
                $"Target step order: {targetStepOrder}", workflow.TenantId), ct);

        _logger.LogInformation("Workflow {WorkflowId} rolled back to step {TargetStepOrder}",
            workflowId, targetStepOrder);
    }

    public async Task<WorkflowStateSnapshot> GetStateAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        var lastCompletedStep = workflow.Steps
            .Where(s => s.State == WorkflowState.Completed)
            .OrderByDescending(s => s.Order)
            .FirstOrDefault();

        var stepSnapshots = workflow.Steps.Select(s => new StepSnapshot(
            s.Id, s.Order, s.StepName, s.State, s.Result, s.ErrorDetail)).ToArray();

        return new WorkflowStateSnapshot(
            workflowId,
            workflow.CurrentState,
            lastCompletedStep?.Order ?? 0,
            stepSnapshots);
    }

    // ──────────── Sequential Preset ────────────

    private async Task RunSequentialAsync(Workflow workflow, CancellationToken ct)
    {
        var orderedSteps = workflow.Steps.OrderBy(s => s.Order).ToList();

        foreach (var step in orderedSteps)
        {
            ct.ThrowIfCancellationRequested();

            // Build the unified WorkflowContext from current workflow state
            var ctx = BuildWorkflowContext(workflow, step, orderedSteps);

            var result = await ExecuteStepWithRetryAsync(workflow, step, ctx, ct);

            switch (result.Outcome)
            {
                case StepOutcome.Success:
                    // Persist step result
                    step.SetResult(result.Output ?? "");
                    workflow.SetState(WorkflowState.Running);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);

                    await _eventBus.PublishAsync(
                        new StepCompleted(workflow.Id, step.Id, step.StepName, step.Order, result.Output, result.Duration), ct);

                    var isLastStep = step.Order == orderedSteps.Last().Order;
                    if (isLastStep)
                    {
                        workflow.Complete();
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        await _eventBus.PublishAsync(
                            new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
                    }
                    break;

                case StepOutcome.FailedRetry:
                    // Retry already exhausted inside ExecuteStepWithRetryAsync
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, step.Id, step.StepName, step.Order,
                            result.ErrorMessage ?? "Retry exhausted", result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, step.StepName,
                        result.ErrorMessage ?? "Retry exhausted", ct);
                    return;

                case StepOutcome.FailedRollback:
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, step.Id, step.StepName, step.Order,
                            result.ErrorMessage, result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, step.StepName,
                        result.ErrorMessage ?? "Unrecoverable error", ct);
                    return;

                case StepOutcome.NeedsIntervention:
                    workflow.SetState(WorkflowState.Paused);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    _logger.LogWarning("Workflow {WorkflowId} paused for intervention at step {StepName}",
                        workflow.Id, step.StepName);
                    return;
            }
        }
    }

    // ──────────── Negotiation Preset ────────────

    private async Task RunNegotiationAsync(Workflow workflow, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var selectionStrategy = scope.ServiceProvider.GetRequiredService<ISelectionStrategy>();
        var terminationCondition = scope.ServiceProvider.GetRequiredService<ITerminationCondition>();

        while (!ct.IsCancellationRequested)
        {
            // Build unified context
            var ctx = BuildWorkflowContext(workflow, null, workflow.Steps.ToList());

            // Check termination before selecting next step
            if (await terminationCondition.ShouldTerminateAsync(ctx, ct))
            {
                _logger.LogInformation("Negotiation terminated via convergence condition for workflow {WorkflowId}",
                    workflow.Id);
                workflow.Complete();
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);
                await _eventBus.PublishAsync(
                    new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
                return;
            }

            // Select next step
            var nextStep = await selectionStrategy.SelectNextAsync(ctx, workflow.Steps, ct);
            if (nextStep == null)
            {
                _logger.LogInformation("No eligible step selected — negotiation complete for workflow {WorkflowId}",
                    workflow.Id);
                workflow.Complete();
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);
                await _eventBus.PublishAsync(
                    new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Steps.Count, workflow.TenantId), ct);
                return;
            }

            // Build step-specific context
            var stepCtx = BuildWorkflowContext(workflow, nextStep, workflow.Steps.ToList());
            var result = await ExecuteStepWithRetryAsync(workflow, nextStep, stepCtx, ct);

            switch (result.Outcome)
            {
                case StepOutcome.Success:
                    nextStep.SetResult(result.Output ?? "");
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    await _eventBus.PublishAsync(
                        new StepCompleted(workflow.Id, nextStep.Id, nextStep.StepName,
                            nextStep.Order, result.Output, result.Duration), ct);
                    break;

                case StepOutcome.FailedRollback:
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, nextStep.Id, nextStep.StepName, nextStep.Order,
                            result.ErrorMessage, result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, nextStep.StepName,
                        result.ErrorMessage ?? "Unrecoverable in negotiation", ct);
                    return;

                case StepOutcome.NeedsIntervention:
                    workflow.SetState(WorkflowState.Paused);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return;

                // FailedRetry — log, persist failure state, and continue selection
                case StepOutcome.FailedRetry:
                    _logger.LogWarning("Step {StepName} failed after retry in negotiation, continuing selection",
                        nextStep.StepName);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, nextStep.Id, nextStep.StepName, nextStep.Order,
                            result.ErrorMessage, result.Duration), ct);
                    break;
            }
        }
    }

    // ──────────── Step Execution Helpers ────────────

    private async Task<StepExecutionResult> ExecuteStepWithRetryAsync(
        Workflow workflow, WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
    {
        var retryCount = 0;
        StepExecutionResult? lastResult = null;
        var maxRetries = _settings.MaxRetryAttempts;

        while (retryCount <= maxRetries)
        {
            ct.ThrowIfCancellationRequested();

            step.SetState(WorkflowState.Running);
            _logger.LogInformation("Executing step {StepName} (attempt {Attempt}/{MaxRetry})",
                step.StepName, retryCount + 1, maxRetries + 1);

            var executor = ResolveExecutor(step);
            if (executor == null)
            {
                step.SetError("No executor found for step: " + step.StepName);
                return StepExecutionResult.FatalFailure(step.ErrorDetail ?? "No executor found");
            }

            var startTime = DateTime.UtcNow;
            try
            {
                var stepTimeout = _settings.StepTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(_settings.StepTimeoutSeconds)
                    : TimeSpan.FromSeconds(StateMachineSettings.DefaultStepTimeoutSeconds);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(stepTimeout);

                lastResult = await executor.ExecuteAsync(step, ctx, timeoutCts.Token);
                var duration = DateTime.UtcNow - startTime;
                lastResult = lastResult with { Duration = duration };

                if (lastResult.Outcome == StepOutcome.Success)
                    return lastResult;

                if (lastResult.Outcome == StepOutcome.NeedsIntervention)
                    return lastResult;

                // Failed (retryable or fatal)
                if (lastResult.Outcome == StepOutcome.FailedRollback)
                {
                    step.SetError(lastResult.ErrorMessage ?? "Unrecoverable error");
                    return lastResult;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var duration = DateTime.UtcNow - startTime;
                lastResult = StepExecutionResult.RetryableFailure(
                    $"Step timed out after {_settings.StepTimeoutSeconds}s", duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                _logger.LogWarning(ex, "Step {StepName} failed on attempt {Attempt}",
                    step.StepName, retryCount + 1);
                lastResult = StepExecutionResult.RetryableFailure(ex.Message, duration);
            }

            retryCount++;

            if (retryCount <= maxRetries)
            {
                _logger.LogInformation("Retrying step {StepName} in {Delay}ms (attempt {Attempt}/{MaxRetry})",
                    step.StepName, _settings.RetryDelayMs, retryCount, maxRetries + 1);
                await Task.Delay(_settings.RetryDelayMs, ct);
                continue; // Keep state as Running for next attempt
            }

            // All retries exhausted — now mark as Failed
            step.SetState(WorkflowState.Failed);
            step.SetError(lastResult?.ErrorMessage ?? "All retry attempts exhausted");
        }

        return lastResult ?? StepExecutionResult.FatalFailure("All retry attempts exhausted");
    }

    private async Task RollbackCompletedStepsAsync(
        Workflow workflow, string failedStepName, string errorDetail, CancellationToken ct)
    {
        // Precise rollback: find the failed step's order and reset ALL steps from
        // that point onward (Blueprint C.6), regardless of their current state.
        var failedStep = workflow.Steps.FirstOrDefault(s => s.StepName == failedStepName);
        int rollbackFromOrder = failedStep?.Order ?? 0;

        foreach (var step in workflow.Steps.Where(s => s.Order >= rollbackFromOrder))
        {
            step.SetState(WorkflowState.Pending);
            _logger.LogInformation("Rolled back step {StepName} (order {Order})", step.StepName, step.Order);
        }

        workflow.SetState(WorkflowState.RolledBack);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name, failedStepName, errorDetail, workflow.TenantId), ct);
    }

    // ──────────── Context Building ────────────

    private WorkflowContext BuildWorkflowContext(
        Workflow workflow, WorkflowStep? currentStep, IReadOnlyList<WorkflowStep> allSteps)
    {
        var artifacts = new Dictionary<string, StepArtifact>();
        var blackboard = Blackboard.Empty;

        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            artifacts[step.StepName] = new StepArtifact
            {
                StepName = step.StepName,
                StepOrder = step.Order,
                Content = step.Result!,
                ContentType = DetectContentType(step.StepName)
            };
        }

        return new WorkflowContext
        {
            WorkflowId = workflow.Id,
            CurrentStepOrder = currentStep?.Order ?? 0,
            Artifacts = artifacts,
            Blackboard = blackboard,
            Retrieval = RetrievalContext.Empty,
            Summary = StepHistory.Empty,
            TenantId = workflow.TenantId
        };
    }

    private static string DetectContentType(string stepName)
    {
        var name = stepName.ToLowerInvariant();
        if (name.Contains("architect")) return "architecture";
        if (name.Contains("developer") || name.Contains("code")) return "code";
        if (name.Contains("test") || name.Contains("qa")) return "test-report";
        if (name.Contains("doc") || name.Contains("writer")) return "documentation";
        if (name.Contains("product") || name.Contains("requirement")) return "requirements";
        return "general";
    }

    private IStepExecutor? ResolveExecutor(WorkflowStep step)
    {
        // Resolve from DI — each call gets current scope's executors
        var executors = _serviceProvider.GetServices<IStepExecutor>().ToList();
        // First try: StepType == step.StepName (exact match)
        var exact = executors.FirstOrDefault(e => e.StepType == step.StepName);
        if (exact != null) return exact;
        // Second try: wildcard glob match (*critic* matches any step name containing "critic")
        var wildcard = executors.FirstOrDefault(e =>
            e.StepType.Length > 1 && e.StepType.Contains('*') &&
            IsGlobMatch(e.StepType, step.StepName));
        if (wildcard != null) return wildcard;
        // Fallback: catch-all "*" executor
        return executors.FirstOrDefault(e => e.StepType == "*")
            ?? executors.FirstOrDefault();
    }

    private static bool IsGlobMatch(string pattern, string value)
    {
        // Simple wildcard: supports * at start, end, or both (e.g. "*critic*", "prefix*", "*suffix")
        if (pattern.StartsWith('*') && pattern.EndsWith('*') && pattern.Length > 2)
            return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*') && pattern.Length > 1)
            return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*') && pattern.Length > 1)
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, value, StringComparison.Ordinal);
    }

    private static OrchestrationPreset DetectPreset(Workflow workflow)
    {
        // Heuristic: if workflow has negotiation-related metadata, use negotiation
        // Otherwise default to sequential (the fast path)
        return workflow.Context?.Contains("\"preset\":\"negotiation\"", StringComparison.OrdinalIgnoreCase) == true
            ? OrchestrationPreset.Negotiation
            : OrchestrationPreset.Sequential;
    }

    private async Task<Workflow> LoadWorkflowAsync(Guid workflowId, CancellationToken ct)
    {
        return await _repository.GetByIdAsync(workflowId, ct)
            ?? throw new InvalidOperationException($"Workflow {workflowId} not found");
    }
}
