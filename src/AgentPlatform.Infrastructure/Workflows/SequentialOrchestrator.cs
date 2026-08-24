using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Diagnostics;
using AgentPlatform.Application.Routing;
using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.ExecutionLogs;
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
/// F30: Supports durable execution with checkpoint persistence and crash recovery.
/// </summary>
internal sealed class SequentialOrchestrator
{
    private readonly IWorkflowRepository _repository;
    private readonly IExecutionLogRepository _executionLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDomainEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly StateMachineSettings _settings;
    private readonly ITokenCounter _tokenCounter;
    private readonly IVectorStore _vectorStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly DurableExecutionSettings _durableSettings;

    private int _stepsSinceLastCheckpoint = 0;
    private DateTime _lastCheckpointTime = DateTime.UtcNow;

    public SequentialOrchestrator(
        IWorkflowRepository repository,
        IExecutionLogRepository executionLogRepository,
        IUnitOfWork unitOfWork,
        IDomainEventBus eventBus,
        IServiceProvider serviceProvider,
        ILogger logger,
        StateMachineSettings settings,
        IVectorStore vectorStore,
        ITokenCounter tokenCounter,
        DurableExecutionSettings durableSettings)
    {
        _repository = repository;
        _executionLogRepository = executionLogRepository;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings;
        _vectorStore = vectorStore;
        _tokenCounter = tokenCounter;
        _durableSettings = durableSettings;
    }

    public async Task RunSequentialAsync(
        Workflow workflow,
        CancellationToken ct,
        IRunningExecutionRepository? runningExecutionRepository = null,
        string? instanceId = null,
        TimeSpan? leaseTtl = null,
        bool resumeFromCheckpoint = false)
    {
        var (executionOrder, loopBodyIds, skip) = PrepareContext(workflow);

        Blackboard blackboard;
        int startIndex = 0;

        if (resumeFromCheckpoint)
        {
            // Restore state from ExecutionLog checkpoint
            var (restoredBlackboard, restoredIndex) = await RestoreFromCheckpointAsync(workflow, executionOrder, ct);
            blackboard = restoredBlackboard;
            startIndex = restoredIndex;
            _logger.LogInformation("Workflow {WorkflowId} resuming from checkpoint at step index {StartIndex}", workflow.Id, startIndex);
        }
        else
        {
            blackboard = SeedTriggerBlackboard(workflow, Blackboard.Empty);
        }

        // If we have a running execution repo, persist initial checkpoint
        if (runningExecutionRepository != null && instanceId != null)
        {
            await PersistCheckpointAsync(workflow, blackboard, executionOrder, skip, loopBodyIds, 0, runningExecutionRepository, instanceId, leaseTtl!.Value, ct);
        }

        await RunToCompletionAsync(
            workflow,
            executionOrder,
            blackboard,
            skip,
            loopBodyIds,
            startIndex,
            runningExecutionRepository,
            instanceId,
            leaseTtl,
            ct);
    }

    // ──────────── F25 调试能力（复用既有拓扑/分支/循环逻辑） ────────────

    public async Task<DebugStepResult> DebugStepAsync(Workflow workflow, Blackboard blackboard, CancellationToken ct)
    {
        var (executionOrder, loopBodyIds, skip) = PrepareContext(workflow);
        var (executed, node, _, _) = await ExecuteNextPendingNodeAsync(workflow, blackboard, skip, loopBodyIds, executionOrder, 0, ct);

        if (!executed)
        {
            if (workflow.CurrentState == WorkflowState.Running)
            {
                workflow.Complete();
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);
                await _eventBus.PublishAsync(
                    new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Nodes.Count, workflow.TenantId), ct);
            }
            return new DebugStepResult(false, workflow.CurrentState, null);
        }

        var snapshot = node is null
            ? null
            : new StepSnapshot(node.Id, node.Order, node.Name, node.State, node.Result, node.ErrorDetail);
        return new DebugStepResult(true, workflow.CurrentState, snapshot);
    }

    public async Task<WorkflowState> DebugResumeAsync(Workflow workflow, Blackboard blackboard, CancellationToken ct)
    {
        var (executionOrder, loopBodyIds, skip) = PrepareContext(workflow);
        await RunToCompletionAsync(workflow, executionOrder, blackboard, skip, loopBodyIds, 0, null, null, null, ct);
        return workflow.CurrentState;
    }

    public async Task<DebugStepResult> DebugRetryNodeAsync(
        Workflow workflow, Guid nodeId, Blackboard blackboard, CancellationToken ct)
    {
        var (executionOrder, loopBodyIds, skip) = PrepareContext(workflow);
        var node = executionOrder.FirstOrDefault(n => n.Id == nodeId)
            ?? throw new InvalidOperationException($"Node '{nodeId}' not found in workflow '{workflow.Id}'.");

        if (node.Type == StepType.Loop && node is WorkflowNode loopNode)
        {
            await RunLoopBodyAsync(workflow, loopNode, executionOrder, blackboard, skip, ct);
            var snap = new StepSnapshot(node.Id, node.Order, node.Name, node.State, node.Result, node.ErrorDetail);
            return new DebugStepResult(true, workflow.CurrentState, snap);
        }

        await RunSingleNodeAsync(workflow, node, blackboard, executionOrder, skip, ct);
        var snapshot = new StepSnapshot(node.Id, node.Order, node.Name, node.State, node.Result, node.ErrorDetail);
        return new DebugStepResult(true, workflow.CurrentState, snapshot);
    }

    // ──────────── 执行上下文准备 ────────────

    private (IReadOnlyList<IWorkflowExecutable> ExecutionOrder, HashSet<Guid> LoopBodyIds, HashSet<Guid> Skip)
        PrepareContext(Workflow workflow)
    {
        workflow.EnsureGraphSynced();

        IReadOnlyList<IWorkflowExecutable> executionOrder = workflow.IsDag
            ? DagExecutionOrder(workflow)
            : workflow.Steps.Cast<IWorkflowExecutable>().ToList();

        var loopBodyIds = new HashSet<Guid>();
        foreach (var n in executionOrder)
        {
            if (n.Type == StepType.Loop && n is WorkflowNode ln)
            {
                foreach (var name in ParseLoopConfig(ln.ConfigJson).BodyNodeNames)
                {
                    var bn = workflow.Nodes.FirstOrDefault(x =>
                        x.Name == name && x.Type is not (StepType.Start or StepType.End));
                    if (bn is not null) loopBodyIds.Add(bn.Id);
                }
            }
        }

        var skip = new HashSet<Guid>();
        foreach (var n in executionOrder)
        {
            if (n.Type == StepType.Condition && n.State == WorkflowState.Completed
                && (string.Equals(n.Result, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n.Result, "false", StringComparison.OrdinalIgnoreCase)))
            {
                ApplyBranchSkip(workflow, n.Id, n.Result!, skip);
            }
        }

        return (executionOrder, loopBodyIds, skip);
    }

    private async Task RunToCompletionAsync(
        Workflow workflow,
        IReadOnlyList<IWorkflowExecutable> executionOrder,
        Blackboard blackboard,
        HashSet<Guid> skip,
        HashSet<Guid> loopBodyIds,
        int startIndex,
        IRunningExecutionRepository? runningExecutionRepository,
        string? instanceId,
        TimeSpan? leaseTtl,
        CancellationToken ct)
    {
        bool executed;
        StepExecutionResult? last;
        int currentIndex = startIndex;

        do
        {
            (executed, _, last, currentIndex) = await ExecuteNextPendingNodeAsync(
                workflow, blackboard, skip, loopBodyIds, executionOrder, currentIndex, ct);

            // Persist checkpoint after each step (batched per F30 D4)
            if (runningExecutionRepository != null && instanceId != null && executed)
            {
                _stepsSinceLastCheckpoint++;
                var now = DateTime.UtcNow;
                var shouldFlush = _stepsSinceLastCheckpoint >= _durableSettings.CheckpointBatchSize
                               || now - _lastCheckpointTime >= TimeSpan.FromSeconds(_durableSettings.CheckpointMaxAgeSeconds)
                               || last?.Outcome is StepOutcome.NeedsIntervention
                                      or StepOutcome.FailedRetry
                                      or StepOutcome.FailedRollback;

                if (shouldFlush)
                {
                    await PersistCheckpointAsync(workflow, blackboard, executionOrder, skip, loopBodyIds,
                        currentIndex, runningExecutionRepository, instanceId, leaseTtl!.Value, ct);
                    _stepsSinceLastCheckpoint = 0;
                    _lastCheckpointTime = now;
                }
            }
        } while (executed && last is not { Outcome: StepOutcome.FailedRetry or StepOutcome.FailedRollback or StepOutcome.NeedsIntervention });

        if (workflow.CurrentState == WorkflowState.Running)
        {
            workflow.Complete();
            _repository.Update(workflow);
            await _unitOfWork.SaveChangesAsync(ct);
            await _eventBus.PublishAsync(
                new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Nodes.Count, workflow.TenantId), ct);

            // Clean up RunningExecution on successful completion
            if (runningExecutionRepository != null && instanceId != null)
            {
                var runningExec = await runningExecutionRepository.GetByWorkflowIdAsync(workflow.Id, ct);
                if (runningExec != null)
                {
                    runningExec.Complete();
                    runningExecutionRepository.Update(runningExec);
                    await _unitOfWork.SaveChangesAsync(ct);
                }
            }
        }
        else if (workflow.CurrentState == WorkflowState.Paused)
        {
            // Ensure final checkpoint is persisted on pause/intervention
            if (runningExecutionRepository != null && instanceId != null)
            {
                await PersistCheckpointAsync(workflow, blackboard, executionOrder, skip, loopBodyIds,
                    currentIndex, runningExecutionRepository, instanceId, leaseTtl!.Value, ct);
            }
        }
    }

    public async Task<(bool Executed, IWorkflowExecutable? Node, StepExecutionResult? Result, int NextIndex)> ExecuteNextPendingNodeAsync(
        Workflow workflow,
        Blackboard blackboard,
        HashSet<Guid> skip,
        HashSet<Guid> loopBodyIds,
        IReadOnlyList<IWorkflowExecutable> executionOrder,
        int startIndex,
        CancellationToken ct)
    {
        for (int i = startIndex; i < executionOrder.Count; i++)
        {
            var node = executionOrder[i];
            if (node.State == WorkflowState.Completed)
                continue;

            if (loopBodyIds.Contains(node.Id))
            {
                _logger.LogDebug("跳过 Loop body 节点 {NodeName}（由 Loop 节点内联执行）", node.Name);
                continue;
            }

            if (skip.Contains(node.Id))
            {
                _logger.LogDebug("跳过非选中分支节点 {NodeName}", node.Name);
                continue;
            }

            ct.ThrowIfCancellationRequested();

            if (node.Type == StepType.Loop && node is WorkflowNode loopNode)
            {
                await RunLoopBodyAsync(workflow, loopNode, executionOrder, blackboard, skip, ct);
                return (true, node, null, i + 1);
            }

            var result = await RunSingleNodeAsync(workflow, node, blackboard, executionOrder, skip, ct);
            return (true, node, result, i + 1);
        }

        return (false, null, null, executionOrder.Count);
    }

    private async Task<StepExecutionResult?> RunSingleNodeAsync(
        Workflow workflow,
        IWorkflowExecutable node,
        Blackboard blackboard,
        IReadOnlyList<IWorkflowExecutable> executionOrder,
        HashSet<Guid> skip,
        CancellationToken ct)
    {
        var ctx = await BuildWorkflowContext(workflow, node, executionOrder, blackboard, ct);
        var result = await ExecuteStepWithRetryAsync(workflow, node, ctx, ct);

        switch (result.Outcome)
        {
            case StepOutcome.Success:
                node.SetResult(result.Output ?? "");
                workflow.SetState(WorkflowState.Running);
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);
                await _eventBus.PublishAsync(
                    new StepCompleted(workflow.Id, node.Id, node.Name, node.Order, result.Output, result.Duration,
                        node.Type, result.Tokens), ct);

                if (node.Type == StepType.Condition)
                    ApplyBranchSkip(workflow, node.Id, node.Result!, skip);

                if (workflow.IsDag) workflow.SyncStepsFromGraph();
                break;

            case StepOutcome.FailedRetry:
                await _eventBus.PublishAsync(
                    new StepFailed(workflow.Id, node.Id, node.Name, node.Order,
                        result.ErrorMessage ?? "Retry exhausted", result.Duration,
                        node.Type, result.Tokens), ct);
                await RollbackCompletedStepsAsync(workflow, node.Order, node.Name,
                    result.ErrorMessage ?? "Retry exhausted", ct);
                break;

            case StepOutcome.FailedRollback:
                await _eventBus.PublishAsync(
                    new StepFailed(workflow.Id, node.Id, node.Name, node.Order,
                        result.ErrorMessage, result.Duration,
                        node.Type, result.Tokens), ct);
                await RollbackCompletedStepsAsync(workflow, node.Order, node.Name,
                    result.ErrorMessage ?? "Unrecoverable error", ct);
                break;

            case StepOutcome.NeedsIntervention:
                workflow.SetState(WorkflowState.Paused);
                _repository.Update(workflow);
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogWarning("Workflow {WorkflowId} paused for intervention at node {NodeName}",
                    workflow.Id, node.Name);
                break;
        }

        return result;
    }

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
        Workflow workflow, IWorkflowExecutable? currentStep, IReadOnlyList<IWorkflowExecutable> allSteps, Blackboard blackboard, CancellationToken ct)
    {
        var artifacts = new Dictionary<string, StepArtifact>();

        foreach (var step in allSteps.Where(s => s.State == WorkflowState.Completed && !string.IsNullOrEmpty(s.Result)))
        {
            if (step.Type == StepType.Start)
                continue;
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
                var ragSettings = _serviceProvider.GetRequiredService<IOptions<AgentPlatform.Application.Abstractions.RagSettings>>().Value;
                var searchResults = await _vectorStore.SearchAsync(
                    AgentPlatform.Application.Routing.RoutingConstants.WorkflowContextVectorCollection, currentStep.Name,
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

    // ──────────── F20 编排器引擎辅助（分支 / 循环） ────────────

    private static void ApplyBranchSkip(Workflow workflow, Guid conditionId, string branch, HashSet<Guid> skip)
    {
        var outEdges = workflow.Edges.Where(e => e.SourceNodeId == conditionId).ToList();
        if (outEdges.Count < 2) return;

        var selected = outEdges.FirstOrDefault(e =>
            string.Equals(e.Label, branch, StringComparison.OrdinalIgnoreCase));
        var nonSelected = outEdges.FirstOrDefault(e =>
            !string.Equals(e.Label, branch, StringComparison.OrdinalIgnoreCase));
        if (selected is null || nonSelected is null) return;

        var reachableNonSelected = ReachableFrom(workflow, nonSelected.TargetNodeId);
        var reachableSelected = ReachableFrom(workflow, selected.TargetNodeId);
        foreach (var id in reachableNonSelected)
            if (!reachableSelected.Contains(id)) skip.Add(id);
    }

    private static HashSet<Guid> ReachableFrom(Workflow workflow, Guid startId)
    {
        var reachable = new HashSet<Guid> { startId };
        var q = new Queue<Guid>();
        q.Enqueue(startId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var next in workflow.Edges.Where(e => e.SourceNodeId == cur).Select(e => e.TargetNodeId))
                if (reachable.Add(next)) q.Enqueue(next);
        }
        return reachable;
    }

    private async Task RunLoopBodyAsync(
        Workflow workflow, WorkflowNode loopNode, IReadOnlyList<IWorkflowExecutable> executionOrder,
        Blackboard blackboard, HashSet<Guid> skip, CancellationToken ct)
    {
        var config = ParseLoopConfig(loopNode.ConfigJson);
        var loopCtx = await BuildWorkflowContext(workflow, loopNode, executionOrder, blackboard, ct);
        var items = ResolveLoopItems(loopCtx, config.ItemsSource);

        var completedBodySteps = 0;
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(config.ItemVariable))
                blackboard.Set(config.ItemVariable, item);

            foreach (var bodyName in config.BodyNodeNames)
            {
                var bodyNode = workflow.Nodes.FirstOrDefault(n =>
                    n.Name == bodyName && n.Type is not (StepType.Start or StepType.End));
                if (bodyNode is null || bodyNode == loopNode) continue;
                if (skip.Contains(bodyNode.Id)) continue;

                bodyNode.Reset();

                var bodyCtx = await BuildWorkflowContext(workflow, bodyNode, executionOrder, blackboard, ct);
                var result = await ExecuteStepWithRetryAsync(workflow, bodyNode, bodyCtx, ct);

                switch (result.Outcome)
                {
                    case StepOutcome.Success:
                        bodyNode.SetResult(result.Output ?? "");
                        workflow.SetState(WorkflowState.Running);
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        await _eventBus.PublishAsync(new StepCompleted(
                            workflow.Id, bodyNode.Id, bodyNode.Name, bodyNode.Order, result.Output, result.Duration,
                            bodyNode.Type, result.Tokens), ct);
                        completedBodySteps++;
                        break;

                    case StepOutcome.NeedsIntervention:
                        workflow.SetState(WorkflowState.Paused);
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        _logger.LogWarning("Loop body 节点 {NodeName} 请求人工干预，工作流暂停", bodyNode.Name);
                        loopNode.SetResult($"loop: 在迭代 {completedBodySteps} 处因 body 节点 {bodyNode.Name} 暂停");
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        return;

                    default:
                        await _eventBus.PublishAsync(new StepFailed(
                            workflow.Id, bodyNode.Id, bodyNode.Name, bodyNode.Order,
                            result.ErrorMessage ?? "Loop body 失败", result.Duration,
                            bodyNode.Type, result.Tokens), ct);
                        await RollbackCompletedStepsAsync(workflow, bodyNode.Order, bodyNode.Name,
                            result.ErrorMessage ?? "Loop body 失败", ct);
                        loopNode.SetResult($"loop: body 节点 {bodyNode.Name} 失败并回滚");
                        _repository.Update(workflow);
                        await _unitOfWork.SaveChangesAsync(ct);
                        return;
                }
            }
        }

        loopNode.SetResult($"loop completed: {items.Count} items, {completedBodySteps} body-steps");
        workflow.SetState(WorkflowState.Running);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
        await _eventBus.PublishAsync(new StepCompleted(
            workflow.Id, loopNode.Id, loopNode.Name, loopNode.Order, loopNode.Result, TimeSpan.Zero,
            loopNode.Type, null), ct);
    }

    private static LoopNodeConfig ParseLoopConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new LoopNodeConfig(null, null, []);

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            string? itemsSource = root.TryGetProperty("itemsSource", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            string? itemVariable = root.TryGetProperty("itemVariable", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
            var body = new List<string>();
            if (root.TryGetProperty("bodyNodeNames", out var b) && b.ValueKind == JsonValueKind.Array)
                foreach (var el in b.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String) body.Add(el.GetString()!);
            return new LoopNodeConfig(itemsSource, itemVariable, body);
        }
        catch (JsonException)
        {
            return new LoopNodeConfig(null, null, []);
        }
    }

    private static IReadOnlyList<string> ResolveLoopItems(WorkflowContext ctx, string? itemsSource)
    {
        if (string.IsNullOrWhiteSpace(itemsSource)) return [];
        var raw = ctx.Blackboard.Get(itemsSource)
                  ?? (ctx.Artifacts.TryGetValue(itemsSource, out var a) ? a.Content : null)
                  ?? itemsSource;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var el in doc.RootElement.EnumerateArray())
                    list.Add(el.ValueKind == JsonValueKind.String ? el.GetString()! : el.GetRawText());
                return list;
            }
        }
        catch (JsonException) { }
        return [];
    }

    private sealed record LoopNodeConfig(string? ItemsSource, string? ItemVariable, IReadOnlyList<string> BodyNodeNames);

    private static Blackboard SeedTriggerBlackboard(Workflow workflow, Blackboard blackboard)
    {
        if (string.IsNullOrWhiteSpace(workflow.Context)) return blackboard;
        try
        {
            using var doc = JsonDocument.Parse(workflow.Context);
            if (!doc.RootElement.TryGetProperty("trigger", out var trigger) || trigger.ValueKind != JsonValueKind.Object)
                return blackboard;

            blackboard = blackboard.Set("trigger", trigger.GetRawText());

            foreach (var prop in trigger.EnumerateObject())
            {
                var flat = FlattenScalar(prop.Value);
                if (flat is not null)
                    blackboard = blackboard.Set($"trigger.{prop.Name}", flat);
            }
            return blackboard;
        }
        catch (JsonException)
        {
            return blackboard;
        }
    }

    private static string? FlattenScalar(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                return el.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return null;
        }
    }

    // ──────────── F30 检查点持久化与恢复 ────────────

    /// <summary>
    /// Restores workflow execution state from the latest ExecutionLog checkpoint.
    /// Returns the restored Blackboard and the execution order index to resume from.
    /// </summary>
    private async Task<(Blackboard Blackboard, int StartIndex)> RestoreFromCheckpointAsync(
        Workflow workflow,
        IReadOnlyList<IWorkflowExecutable> executionOrder,
        CancellationToken ct)
    {
        // Get the latest ExecutionLog for this workflow
        var executionLogs = await _executionLogRepository.GetByWorkflowIdAsync(workflow.Id, ct);
        var latestLog = executionLogs.FirstOrDefault();
        if (latestLog == null || string.IsNullOrEmpty(latestLog.CheckpointData))
        {
            _logger.LogWarning("No checkpoint data found for workflow {WorkflowId}, starting from beginning", workflow.Id);
            return (SeedTriggerBlackboard(workflow, Blackboard.Empty), 0);
        }

        try
        {
            var checkpoint = JsonSerializer.Deserialize<ExecutionCheckpoint>(latestLog.CheckpointData!);
            if (checkpoint == null)
            {
                _logger.LogWarning("Failed to deserialize checkpoint for workflow {WorkflowId}", workflow.Id);
                return (SeedTriggerBlackboard(workflow, Blackboard.Empty), 0);
            }

            // Restore Blackboard
            var blackboard = Blackboard.Empty;
            if (checkpoint.Blackboard != null)
            {
                foreach (var kvp in checkpoint.Blackboard)
                {
                    blackboard = blackboard.Set(kvp.Key, kvp.Value);
                }
            }

            // Restore node states (Completed nodes should stay Completed)
            if (checkpoint.StepStates != null)
            {
                foreach (var stepState in checkpoint.StepStates)
                {
                    var node = executionOrder.FirstOrDefault(n => n.Id == stepState.NodeId);
                    if (node != null && stepState.State == WorkflowState.Completed)
                    {
                        node.SetState(WorkflowState.Completed);
                        node.SetResult(stepState.Result ?? "");
                    }
                }
            }

            int startIndex = checkpoint.ExecutionOrderIndex >= 0 ? checkpoint.ExecutionOrderIndex : 0;
            _logger.LogInformation("Restored checkpoint for workflow {WorkflowId}: version {Version}, index {Index}",
                workflow.Id, checkpoint.CheckpointVersion, startIndex);

            return (blackboard, startIndex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse checkpoint JSON for workflow {WorkflowId}", workflow.Id);
            return (SeedTriggerBlackboard(workflow, Blackboard.Empty), 0);
        }
    }

    /// <summary>
    /// Persists a checkpoint with current execution state to ExecutionLog and RunningExecution.
    /// </summary>
    private async Task PersistCheckpointAsync(
        Workflow workflow,
        Blackboard blackboard,
        IReadOnlyList<IWorkflowExecutable> executionOrder,
        HashSet<Guid> skip,
        HashSet<Guid> loopBodyIds,
        int currentIndex,
        IRunningExecutionRepository runningExecutionRepository,
        string instanceId,
        TimeSpan leaseTtl,
        CancellationToken ct)
    {
        // Build checkpoint data
        var stepStates = executionOrder
            .Where(n => n.State != WorkflowState.Pending)
            .Select(n => new CheckpointStepState
            {
                NodeId = n.Id,
                State = n.State,
                Result = n.Result
            })
            .ToList();

        var checkpoint = new ExecutionCheckpoint
        {
            SchemaVersion = 1,
            CheckpointVersion = (await GetLatestCheckpointVersionAsync(workflow.Id, ct)) + 1,
            Blackboard = blackboard.Entries.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            ExecutionOrderIndex = currentIndex,
            LoopBodyIndices = loopBodyIds.ToDictionary(id => id, _ => 0),
            SkipSet = skip.ToList(),
            StepStates = stepStates,
            TenantId = workflow.TenantId,
            WorkflowId = workflow.Id,
            CapturedAt = DateTime.UtcNow
        };

        var checkpointJson = JsonSerializer.Serialize(checkpoint);

        // Update ExecutionLog
        var executionLog = await GetLatestExecutionLogAsync(workflow.Id, ct);
        if (executionLog != null)
        {
            executionLog.UpdateCheckpoint(checkpointJson);
            _executionLogRepository.Update(executionLog);
        }

        // Update RunningExecution heartbeat
        var runningExec = await runningExecutionRepository.GetByWorkflowIdAsync(workflow.Id, ct);
        if (runningExec != null)
        {
            // Renew lease
            runningExec.TryRenewLease(instanceId, leaseTtl);
            runningExec.UpdateHeartbeat(checkpoint.CheckpointVersion, JsonSerializer.Serialize(blackboard.Entries.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
            runningExecutionRepository.Update(runningExec);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<ExecutionLog?> GetLatestExecutionLogAsync(Guid workflowId, CancellationToken ct)
    {
        var logs = await _executionLogRepository.GetByWorkflowIdAsync(workflowId, ct);
        return logs.FirstOrDefault();
    }

    private async Task<int> GetLatestCheckpointVersionAsync(Guid workflowId, CancellationToken ct)
    {
        var log = await GetLatestExecutionLogAsync(workflowId, ct);
        return log?.CheckpointVersion ?? 0;
    }

    // ──────────── Checkpoint Data Structures ────────────

    private sealed record ExecutionCheckpoint
    {
        public int SchemaVersion { get; init; } = 1;
        public int CheckpointVersion { get; init; }
        public Dictionary<string, string>? Blackboard { get; init; }
        public int ExecutionOrderIndex { get; init; }
        public Dictionary<Guid, int> LoopBodyIndices { get; init; } = new();
        public List<Guid> SkipSet { get; init; } = new();
        public List<CheckpointStepState> StepStates { get; init; } = new();
        public Guid TenantId { get; init; }
        public Guid WorkflowId { get; init; }
        public DateTime CapturedAt { get; init; }
    }

    private sealed record CheckpointStepState
    {
        public Guid NodeId { get; init; }
        public WorkflowState State { get; init; }
        public string? Result { get; init; }
    }
}