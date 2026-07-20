using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading;

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
    private readonly IVectorStore _vectorStore;

    // Tracks in-flight runs so PauseAsync can interrupt them (Blueprint C.7: mid-execution pause).
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> s_runningCts = new();

    // Tracks the preset chosen on first RunAsync so Resume/Retry reuse the SAME preset
    // instead of re-sniffing Context (which could flip the preset mid-lifecycle).
    // Cold-start fallback (e.g. after a process restart) still uses DetectPreset.
    private static readonly ConcurrentDictionary<Guid, OrchestrationPreset> s_resolvedPresets = new();

    public OrchestrationPrimitive(
        IWorkflowRepository repository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        IOptions<StateMachineSettings> settings,
        ILogger<OrchestrationPrimitive> logger,
        IVectorStore vectorStore)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _vectorStore = vectorStore;
    }

    public async Task<Workflow> RunAsync(Workflow workflow, OrchestrationPreset preset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        // Remember the chosen preset for this workflow so Resume/Retry stay stable.
        s_resolvedPresets[workflow.Id] = preset;

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

        // Register a cancellable source so PauseAsync can interrupt an in-flight run (Blueprint C.7).
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        s_runningCts[workflow.Id] = linkedCts;
        var linkedToken = linkedCts.Token;
        try
        {
            switch (preset)
            {
                case OrchestrationPreset.Sequential:
                    await RunSequentialAsync(workflow, linkedToken);
                    break;
                case OrchestrationPreset.Negotiation:
                    await RunNegotiationAsync(workflow, linkedToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset), preset, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout (inner CTS fired) OR external Pause (linked CTS cancelled) —
            // both leave the workflow resumable.
            _logger.LogWarning("Workflow {WorkflowId} execution interrupted (timeout or pause)", workflow.Id);
            workflow.SetState(WorkflowState.Paused);
            _repository.Update(workflow);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Explicit cancellation (outer ct was cancelled mid-execution)
            // Set Paused so the workflow is resumable — otherwise it stays Running permanently.
            _logger.LogWarning("Workflow {WorkflowId} execution was cancelled by caller", workflow.Id);
            workflow.SetState(WorkflowState.Paused);
            _repository.Update(workflow);
            // Use CancellationToken.None because ct is already cancelled
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            s_runningCts.TryRemove(workflow.Id, out _);
            linkedCts.Dispose();
        }

        return workflow;
    }

    public async Task PauseAsync(Guid workflowId, CancellationToken ct = default)
    {
        var workflow = await LoadWorkflowAsync(workflowId, ct);
        if (workflow.CurrentState != WorkflowState.Running)
            throw new InvalidOperationException($"Workflow {workflowId} is not running (state: {workflow.CurrentState})");

        // Interrupt the in-flight run, if one is registered, so an executing step aborts
        // immediately instead of finishing (Blueprint C.7: mid-execution pause).
        if (s_runningCts.TryGetValue(workflowId, out var cts))
            cts.Cancel();

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

        // Reload preset — prefer the one chosen on first RunAsync (stable across resume).
        var preset = ResolvePreset(workflow, workflowId);
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

        // Re-run from the reset step, reusing the stable preset.
        workflow.SetState(WorkflowState.Running);
        await RunAsync(workflow, ResolvePreset(workflow, workflowId), ct);
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
                $"Rolled back to step order {targetStepOrder}",
                $"Precise rollback to step order {targetStepOrder}", workflow.TenantId), ct);

        _logger.LogInformation("Workflow {WorkflowId} rolled back to step order {TargetStepOrder} (Blueprint C.6 precision rollback)",
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
            // Resume continuity (Blueprint C.7): never re-execute steps that already completed.
            // This is what makes a restarted/crashed workflow resume from the last
            // completed step instead of replaying the whole pipeline.
            if (step.State == WorkflowState.Completed)
                continue;

            ct.ThrowIfCancellationRequested();

            // Build the unified WorkflowContext from current workflow state
            var ctx = await BuildWorkflowContext(workflow, step, orderedSteps, ct);

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
                    await RollbackCompletedStepsAsync(workflow, step.Order, step.StepName,
                        result.ErrorMessage ?? "Retry exhausted", ct);
                    return;

                case StepOutcome.FailedRollback:
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, step.Id, step.StepName, step.Order,
                            result.ErrorMessage, result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, step.Order, step.StepName,
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
            var ctx = await BuildWorkflowContext(workflow, null, workflow.Steps.ToList(), ct);

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
            var stepCtx = await BuildWorkflowContext(workflow, nextStep, workflow.Steps.ToList(), ct);
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
                    await RollbackCompletedStepsAsync(workflow, nextStep.Order, nextStep.StepName,
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
        // MaxRetryAttempts = MAXIMUM TOTAL attempts for a step (first attempt + retries).
        // E.g. MaxRetryAttempts = 3 means at most 3 runs, never 4. The explicit `for`
        // loop removes the previous `<=`-bound off-by-one ambiguity.
        var maxAttempts = Math.Max(1, _settings.MaxRetryAttempts);
        StepExecutionResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            step.SetState(WorkflowState.Running);
            _logger.LogInformation("Executing step {StepName} (attempt {Attempt}/{MaxAttempts})",
                step.StepName, attempt, maxAttempts);

            // Record active step metric (tracks step concurrency distribution)
            WorkflowMetrics.ActiveStepsHistogram.Record(1,
                new KeyValuePair<string, object?>("step_name", step.StepName),
                new KeyValuePair<string, object?>("workflow_id", workflow.Id));

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

                // Record step completion (active steps = 0 for this step)
                WorkflowMetrics.ActiveStepsHistogram.Record(0,
                    new KeyValuePair<string, object?>("step_name", step.StepName),
                    new KeyValuePair<string, object?>("workflow_id", workflow.Id));

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
                    step.StepName, attempt);
                lastResult = StepExecutionResult.RetryableFailure(ex.Message, duration);
            }

            // Not the final attempt yet → brief backoff before retrying.
            if (attempt < maxAttempts)
            {
                _logger.LogInformation("Retrying step {StepName} in {Delay}ms (attempt {NextAttempt}/{MaxAttempts})",
                    step.StepName, _settings.RetryDelayMs, attempt + 1, maxAttempts);
                await Task.Delay(_settings.RetryDelayMs, ct);
            }
        }

        // All attempts exhausted — persist the failed state.
        step.SetState(WorkflowState.Failed);
        step.SetError(lastResult?.ErrorMessage ?? "All retry attempts exhausted");
        return lastResult ?? StepExecutionResult.FatalFailure("All retry attempts exhausted");
    }

    private async Task RollbackCompletedStepsAsync(
        Workflow workflow, int failedStepOrder, string failedStepName, string errorDetail, CancellationToken ct)
    {
        // Precise rollback: reset ALL steps from the failed step onward
        // (Blueprint C.6), regardless of their current state. Uses step Order
        // for precision (StepName is not guaranteed unique).
        foreach (var step in workflow.Steps.Where(s => s.Order >= failedStepOrder))
        {
            step.SetState(WorkflowState.Pending);
            _logger.LogInformation("Rolled back step {StepName} (order {Order})", step.StepName, step.Order);
        }

        workflow.SetState(WorkflowState.RolledBack);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name, failedStepName,
                $"Rolled back from step order {failedStepOrder}: {errorDetail}", workflow.TenantId), ct);
        _logger.LogInformation("Workflow {WorkflowId} rolled back from step order {FailedStepOrder}: {ErrorDetail}",
            workflow.Id, failedStepOrder, errorDetail);
    }

    // ──────────── Context Building ────────────

    private async Task<WorkflowContext> BuildWorkflowContext(
        Workflow workflow, WorkflowStep? currentStep, IReadOnlyList<WorkflowStep> allSteps, CancellationToken ct)
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

        // Populate Retrieval from vector store (Blueprint C.3.1)
        var retrieval = RetrievalContext.Empty;
        if (currentStep != null)
        {
            try
            {
                // Await asynchronously with the real cancellation token — do NOT block
                // the thread via .GetAwaiter().GetResult() and do NOT pass ct: default.
                var searchResults = await _vectorStore.SearchAsync(
                    "workflow-context", currentStep.StepName, topK: 3, ct);
                if (searchResults.Count > 0)
                {
                    retrieval = new RetrievalContext
                    {
                        Chunks = searchResults.Select(r => r.Content).ToList(),
                        Sources = searchResults.Select(r => r.DocumentId).ToList()
                    };
                }
            }
            catch (Exception ex)
            {
                // Retrieval is best-effort enrichment: degrade to empty context, but
                // surface the failure at Warning level (was silently swallowed at Debug).
                _logger.LogWarning(ex, "Vector store retrieval failed for step {StepName}; using empty context",
                    currentStep.StepName);
            }
        }

        // Build compressed summary from completed step artifacts (Blueprint C.3.1)
        var summaries = new Dictionary<int, string>();
        const int maxSummaryTokens = 8000;
        var estimatedTokens = 0;
        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            var summary = $"[{step.Order}] {step.StepName}: {Truncate(step.Result!, 200)}";
            var estimatedStepTokens = summary.Length / 2;
            if (estimatedTokens + estimatedStepTokens > maxSummaryTokens)
                break;
            summaries[step.Order] = summary;
            estimatedTokens += estimatedStepTokens;
        }

        return new WorkflowContext
        {
            WorkflowId = workflow.Id,
            CurrentStepOrder = currentStep?.Order ?? 0,
            Artifacts = artifacts,
            Blackboard = blackboard,
            Retrieval = retrieval,
            Summary = new StepHistory
            {
                Summaries = summaries,
                MaxTokens = maxSummaryTokens
            },
            TenantId = workflow.TenantId
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

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

    private OrchestrationPreset ResolvePreset(Workflow workflow, Guid workflowId)
    {
        // Prefer the preset recorded when the workflow first ran; fall back to
        // heuristic sniffing only for cold starts (in-memory cache lost on restart).
        return s_resolvedPresets.TryGetValue(workflowId, out var cached)
            ? cached
            : DetectPreset(workflow);
    }

    private static OrchestrationPreset DetectPreset(Workflow workflow)
    {
        // Cold-start fallback ONLY (ResolvePreset prefers the cached choice).
        // Heuristic: a structured marker in Context selects negotiation; otherwise
        // default to sequential (the fast path). Anchored to the exact JSON key so a
        // step name/result containing the word "negotiation" cannot trigger it.
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
