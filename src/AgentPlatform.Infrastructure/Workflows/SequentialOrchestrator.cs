using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Application.Routing;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Handles sequential preset orchestration: topological (DAG) step execution with retry,
/// rollback, and context building (Blueprint C.2 — Sequential preset).
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
        workflow.EnsureGraphSynced();

        // Legacy workflows execute over the ordered Steps projection (order 0,1,2, no
        // Start/End markers). Explicit DAGs execute over topological node order, skipping
        // the Start entry marker and the End terminal sink.
        IReadOnlyList<IWorkflowExecutable> executionOrder = workflow.IsDag
            ? DagExecutionOrder(workflow)
            : workflow.Steps.Cast<IWorkflowExecutable>().ToList();

        foreach (var node in executionOrder)
        {
            if (node.State == WorkflowState.Completed)
                continue;

            ct.ThrowIfCancellationRequested();

            var ctx = await BuildWorkflowContext(workflow, node, executionOrder, ct);
            var result = await ExecuteStepWithRetryAsync(workflow, node, ctx, ct);

            switch (result.Outcome)
            {
                case StepOutcome.Success:
                    node.SetResult(result.Output ?? "");
                    workflow.SetState(WorkflowState.Running);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    await _eventBus.PublishAsync(
                        new StepCompleted(workflow.Id, node.Id, node.Name, node.Order, result.Output, result.Duration), ct);

                    if (node == executionOrder[^1])
                    {
                        workflow.Complete();
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        await _eventBus.PublishAsync(
                            new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Nodes.Count, workflow.TenantId), ct);
                    }

                    // Reflect node state in the legacy Steps projection for explicit DAGs.
                    if (workflow.IsDag) workflow.SyncStepsFromGraph();
                    break;

                case StepOutcome.FailedRetry:
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, node.Id, node.Name, node.Order,
                            result.ErrorMessage ?? "Retry exhausted", result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, node.Order, node.Name,
                        result.ErrorMessage ?? "Retry exhausted", ct);
                    return;

                case StepOutcome.FailedRollback:
                    await _eventBus.PublishAsync(
                        new StepFailed(workflow.Id, node.Id, node.Name, node.Order,
                            result.ErrorMessage, result.Duration), ct);
                    await RollbackCompletedStepsAsync(workflow, node.Order, node.Name,
                        result.ErrorMessage ?? "Unrecoverable error", ct);
                    return;

                case StepOutcome.NeedsIntervention:
                    workflow.SetState(WorkflowState.Paused);
                    _repository.Update(workflow);
                    await _unitOfWork.SaveChangesAsync(ct);
                    _logger.LogWarning("Workflow {WorkflowId} paused for intervention at node {NodeName}",
                        workflow.Id, node.Name);
                    return;
            }
        }
    }

    /// <summary>
    /// Returns the topological node order for an explicit DAG, with each node's
    /// <see cref="WorkflowNode.Order"/> set to its position, excluding the Start marker
    /// and End terminal sink (which are not executed as steps).
    /// </summary>
    private static IReadOnlyList<IWorkflowExecutable> DagExecutionOrder(Workflow workflow)
    {
        var topo = workflow.GetTopologicalOrder();
        for (var i = 0; i < topo.Count; i++)
            topo[i].SetOrder(i);
        return topo
            .Where(n => n.Type is not StepType.Start and not StepType.End)
            .Cast<IWorkflowExecutable>()
            .ToList();
    }

    public async Task<StepExecutionResult> ExecuteStepWithRetryAsync(
        Workflow workflow, IWorkflowExecutable step, WorkflowContext ctx, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, _settings.MaxRetryAttempts);
        StepExecutionResult? lastResult = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            step.SetState(WorkflowState.Running);
            _logger.LogInformation("Executing step {StepName} (attempt {Attempt}/{MaxAttempts})",
                step.Name, attempt, maxAttempts);

            WorkflowMetrics.ActiveStepsHistogram.Record(1,
                new KeyValuePair<string, object?>("step_name", step.Name),
                new KeyValuePair<string, object?>("workflow_id", workflow.Id));

            var executor = ResolveExecutor(step);
            if (executor == null)
            {
                step.SetError("No executor found for step: " + step.Name);
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
                    new KeyValuePair<string, object?>("step_name", step.Name),
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
                    step.Name, attempt);
                lastResult = StepExecutionResult.RetryableFailure(ex.Message, duration);
            }

            if (attempt < maxAttempts)
            {
                _logger.LogInformation("Retrying step {StepName} in {Delay}ms (attempt {NextAttempt}/{MaxAttempts})",
                    step.Name, _settings.RetryDelayMs, attempt + 1, maxAttempts);
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
        var steps = workflow.IsDag
            ? workflow.Nodes.Cast<IWorkflowExecutable>()
            : workflow.Steps.Cast<IWorkflowExecutable>();
        foreach (var step in steps.Where(s => s.Order >= failedStepOrder))
        {
            step.SetState(WorkflowState.Pending);
            _logger.LogInformation("Rolled back node {NodeName} (order {Order})", step.Name, step.Order);
        }

        // Reflect rolled-back node state in the legacy Steps projection for explicit DAGs.
        if (workflow.IsDag) workflow.SyncStepsFromGraph();

        workflow.SetState(WorkflowState.RolledBack);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new WorkflowRolledBack(workflow.Id, workflow.Name, failedStepName,
                $"Rolled back from node order {failedStepOrder}: {errorDetail}", workflow.TenantId), ct);
        _logger.LogInformation("Workflow {WorkflowId} rolled back from node order {FailedStepOrder}: {ErrorDetail}",
            workflow.Id, failedStepOrder, errorDetail);
    }

    private async Task<WorkflowContext> BuildWorkflowContext(
        Workflow workflow, IWorkflowExecutable? currentStep, IReadOnlyList<IWorkflowExecutable> allSteps, CancellationToken ct)
    {
        var artifacts = new Dictionary<string, StepArtifact>();
        var blackboard = Blackboard.Empty;

        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            if (step.Type == StepType.Start)
                continue; // entry marker, not a real artifact
            artifacts[step.Name] = new StepArtifact
            {
                StepName = step.Name,
                StepOrder = step.Order,
                Content = step.Result!,
                ContentType = DetectContentType(step.Name)
            };
        }

        var retrieval = RetrievalContext.Empty;
        if (currentStep != null)
        {
            try
            {
                var tenantProvider = _serviceProvider.GetRequiredService<ITenantProvider>();
                var ragSettings = _serviceProvider.GetRequiredService<IOptions<RagSettings>>().Value;
                var searchResults = await _vectorStore.SearchAsync(
                    RoutingConstants.WorkflowContextVectorCollection, currentStep.Name,
                    tenantProvider.GetTenantId(), topK: 3, minScore: ragSettings.DefaultMinScore, ct);
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
                    currentStep.Name);
            }
        }

        var summaries = new Dictionary<int, string>();
        var maxSummaryTokens = _settings.MaxSummaryTokens;
        var estimatedTokens = 0;
        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            if (step.Type == StepType.Start)
                continue;
            var summary = $"[{step.Order}] {step.Name}: {StringHelpers.Truncate(step.Result!, 200)}";
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

    private IStepExecutor? ResolveExecutor(IWorkflowExecutable step)
    {
        var executors = _serviceProvider.GetServices<IStepExecutor>().ToList();
        if (step.Type.HasValue)
        {
            var byType = executors.FirstOrDefault(e => e.HandlesType == step.Type.Value);
            if (byType != null) return byType;
        }
        var exact = executors.FirstOrDefault(e => e.StepType == step.Name);
        if (exact != null) return exact;
        var wildcard = executors.FirstOrDefault(e =>
            e.StepType.Length > 1 && e.StepType.Contains('*') &&
            IsGlobMatch(e.StepType, step.Name));
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
