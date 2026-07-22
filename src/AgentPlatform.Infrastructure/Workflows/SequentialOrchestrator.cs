using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Handles sequential preset orchestration: fixed-order step execution with retry,
/// rollback, and context building (Blueprint C.2 �?Sequential preset).
/// </summary>
internal sealed class SequentialOrchestrator
{
    private readonly IWorkflowRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly StateMachineSettings _settings;
    private readonly ITokenCounter _tokenCounter;
    private readonly IVectorStore _vectorStore;
    private readonly IServiceProvider _serviceProvider;

    public SequentialOrchestrator(
        IWorkflowRepository repository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        ILogger logger,
        StateMachineSettings settings,
        IVectorStore vectorStore,
        ITokenCounter tokenCounter)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings;
        _vectorStore = vectorStore;
        _tokenCounter = tokenCounter;
    }

    public async Task RunSequentialAsync(Workflow workflow, CancellationToken ct)
    {
        var orderedSteps = workflow.Steps.OrderBy(s => s.Order).ToList();

        foreach (var step in orderedSteps)
        {
            // Resume continuity (Blueprint C.7): never re-execute steps that already completed.
            if (step.State == WorkflowState.Completed)
                continue;

            ct.ThrowIfCancellationRequested();

            var ctx = await BuildWorkflowContext(workflow, step, orderedSteps, ct);

            var result = await ExecuteStepWithRetryAsync(workflow, step, ctx, ct);

            switch (result.Outcome)
            {
                case StepOutcome.Success:
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

    public async Task<StepExecutionResult> ExecuteStepWithRetryAsync(
        Workflow workflow, WorkflowStep step, WorkflowContext ctx, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, _settings.MaxRetryAttempts);
        StepExecutionResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            step.SetState(WorkflowState.Running);
            _logger.LogInformation("Executing step {StepName} (attempt {Attempt}/{MaxAttempts})",
                step.StepName, attempt, maxAttempts);

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

                WorkflowMetrics.ActiveStepsHistogram.Record(0,
                    new KeyValuePair<string, object?>("step_name", step.StepName),
                    new KeyValuePair<string, object?>("workflow_id", workflow.Id));

                if (lastResult.Outcome == StepOutcome.Success)
                    return lastResult;

                if (lastResult.Outcome == StepOutcome.NeedsIntervention)
                    return lastResult;

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

            if (attempt < maxAttempts)
            {
                _logger.LogInformation("Retrying step {StepName} in {Delay}ms (attempt {NextAttempt}/{MaxAttempts})",
                    step.StepName, _settings.RetryDelayMs, attempt + 1, maxAttempts);
                await Task.Delay(_settings.RetryDelayMs, ct);
            }
        }

        step.SetState(WorkflowState.Failed);
        step.SetError(lastResult?.ErrorMessage ?? "All retry attempts exhausted");
        return lastResult ?? StepExecutionResult.FatalFailure("All retry attempts exhausted");
    }

    private async Task RollbackCompletedStepsAsync(
        Workflow workflow, int failedStepOrder, string failedStepName, string errorDetail, CancellationToken ct)
    {
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

        var retrieval = RetrievalContext.Empty;
        if (currentStep != null)
        {
            try
            {
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
                _logger.LogWarning(ex, "Vector store retrieval failed for step {StepName}; using empty context",
                    currentStep.StepName);
            }
        }

        var summaries = new Dictionary<int, string>();
        var maxSummaryTokens = _settings.MaxSummaryTokens;
        var estimatedTokens = 0;
        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            var summary = $"[{step.Order}] {step.StepName}: {StringHelpers.Truncate(step.Result!, 200)}";
            var estimatedStepTokens = _tokenCounter.CountTokens(summary);
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
                MaxTokens = maxSummaryTokens,
                EstimatedTokenCount = estimatedTokens
            },
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
        var executors = _serviceProvider.GetService(typeof(IEnumerable<IStepExecutor>)) is IEnumerable<IStepExecutor> list
            ? list.ToList()
            : [];
        var exact = executors.FirstOrDefault(e => e.StepType == step.StepName);
        if (exact != null) return exact;
        var wildcard = executors.FirstOrDefault(e =>
            e.StepType.Length > 1 && e.StepType.Contains('*') &&
            IsGlobMatch(e.StepType, step.StepName));
        if (wildcard != null) return wildcard;
        return executors.FirstOrDefault(e => e.StepType == "*")
            ?? executors.FirstOrDefault();
    }

    private static bool IsGlobMatch(string pattern, string value)
    {
        if (pattern.StartsWith('*') && pattern.EndsWith('*') && pattern.Length > 2)
            return value.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*') && pattern.Length > 1)
            return value.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*') && pattern.Length > 1)
            return value.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return string.Equals(pattern, value, StringComparison.Ordinal);
    }
}

