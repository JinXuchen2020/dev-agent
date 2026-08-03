using System.Text.Json;
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

        // F20：单次运行维护单一共享 Blackboard，使 Variable/Loop 跨节点读写生效。
        var blackboard = Blackboard.Empty;

        // F21：若工作流共享 Context 含 `trigger` 信封（由触发器注入），将其落入 Blackboard，
        // 使节点可通过 {{trigger}} / {{trigger.*}} 占位符消费触发载荷（Webhook body / Chat 消息 / 调度元数据）。
        // 仅当 `trigger` 键存在时生效，遗留工作流（Context 为 {}）完全不受影响。
        blackboard = SeedTriggerBlackboard(workflow, blackboard);

        // F20：Loop body 节点仅由各自 Loop 节点内联执行，主线性遍历需跳过（避免脱离循环上下文重复执行）。
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

        // F20：条件分支跳过集合（非选中分支的可达子图，排除与选中分支/join 重叠的节点）。
        var skip = new HashSet<Guid>();

        // 续跑场景：已完成的 Condition 节点需重算 skip（暂停前的分支决策在内存中已丢失）。
        foreach (var n in executionOrder)
        {
            if (n.Type == StepType.Condition && n.State == WorkflowState.Completed
                && (string.Equals(n.Result, "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n.Result, "false", StringComparison.OrdinalIgnoreCase)))
            {
                ApplyBranchSkip(workflow, n.Id, n.Result!, skip);
            }
        }

        foreach (var node in executionOrder)
        {
            if (node.State == WorkflowState.Completed)
                continue;

            // F20：Loop body 节点由 Loop 节点内联驱动，主线性遍历跳过。
            if (loopBodyIds.Contains(node.Id))
            {
                _logger.LogDebug("跳过 Loop body 节点 {NodeName}（由 Loop 节点内联执行）", node.Name);
                continue;
            }

            // F20：非选中分支的节点跳过（保持 Pending；工作流完成判定在循环结束后统一处理）。
            if (skip.Contains(node.Id))
            {
                _logger.LogDebug("跳过非选中分支节点 {NodeName}", node.Name);
                continue;
            }

            ct.ThrowIfCancellationRequested();

            // F20：Loop 节点在编排器内联执行 body（共享 Blackboard 注入 itemVariable），
            // 不经由独立执行器（类比 Start/End 这类结构型节点，仅承载语义、不产生自身 artifact 副作用）。
            if (node.Type == StepType.Loop && node is WorkflowNode loopNode)
            {
                await RunLoopBodyAsync(workflow, loopNode, executionOrder, blackboard, skip, ct);
                continue;
            }

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
                        new StepCompleted(workflow.Id, node.Id, node.Name, node.Order, result.Output, result.Duration), ct);

                    // F20：Condition 分支——执行成功后按结果计算非选中分支的 skip 集合。
                    if (node.Type == StepType.Condition)
                        ApplyBranchSkip(workflow, node.Id, node.Result!, skip);

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

        // F20：完成判定——循环结束后若仍 Running（所有非跳过节点均已执行），标记 Completed。
        // 此前「最后一个节点即完成」的逻辑在分支跳过末端节点时会漏判，故改为循环后统一判定。
        if (workflow.CurrentState == WorkflowState.Running)
        {
            workflow.Complete();
            _repository.Update(workflow);
            await _unitOfWork.SaveChangesAsync(ct);
            await _eventBus.PublishAsync(
                new WorkflowCompleted(workflow.Id, workflow.Name, workflow.Nodes.Count, workflow.TenantId), ct);
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
        Workflow workflow, IWorkflowExecutable? currentStep, IReadOnlyList<IWorkflowExecutable> allSteps, Blackboard blackboard, CancellationToken ct)
    {
        var artifacts = new Dictionary<string, StepArtifact>();

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

    // ──────────── F20 编排器引擎辅助（分支 / 循环） ────────────

    /// <summary>
    /// 按 Condition 节点的选中分支结果，将「非选中分支的可达子图」加入 skip 集合，
    /// 但排除同时可由选中分支到达的 join 节点（避免误跳汇合点）。幂等。
    /// </summary>
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

    /// <summary>从 <paramref name="startId"/> 出发、沿有向边可达的全部节点集合（含起点）。</summary>
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

    /// <summary>
    /// 内联执行 Loop 节点的 body：对每个 item 将 <c>itemVariable</c> 注入共享 Blackboard，
    /// 然后顺序执行 body 子图节点（共享可变 Blackboard 携带每轮 item 值）。body 节点在线性主循环中被标 Completed 后跳过，避免重复执行。
    /// </summary>
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
                // F20：每个迭代都要重新执行 body（清除上一轮的 Completed 状态与结果），
                // 使 body 真正按 item 逐项运行，而非仅在首个 item 跑一次。
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
                            workflow.Id, bodyNode.Id, bodyNode.Name, bodyNode.Order, result.Output, result.Duration), ct);
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

                    default: // FailedRetry / FailedRollback
                        await _eventBus.PublishAsync(new StepFailed(
                            workflow.Id, bodyNode.Id, bodyNode.Name, bodyNode.Order,
                            result.ErrorMessage ?? "Loop body 失败", result.Duration), ct);
                        await RollbackCompletedStepsAsync(workflow, bodyNode.Order, bodyNode.Name,
                            result.ErrorMessage ?? "Loop body 失败", ct);
                        return;
                }
            }
        }

        loopNode.SetResult($"loop completed: {items.Count} items, {completedBodySteps} body-steps");
        workflow.SetState(WorkflowState.Running);
        _repository.Update(workflow);
        await _unitOfWork.SaveChangesAsync(ct);
        await _eventBus.PublishAsync(new StepCompleted(
            workflow.Id, loopNode.Id, loopNode.Name, loopNode.Order, loopNode.Result, TimeSpan.Zero), ct);
    }

    /// <summary>解析 Loop 节点配置（<c>itemsSource</c> / <c>itemVariable</c> / <c>bodyNodeNames</c>）。</summary>
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

    /// <summary>
    /// 解析循环项集合：优先取 Blackboard / Artifact 中同名的 JSON 数组，否则将
    /// <paramref name="itemsSource"/> 作为字面量 JSON 数组解析。返回每项序列化后的字符串。
    /// </summary>
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

    /// <summary>
    /// F21：将工作流共享 Context 中的 `trigger` 信封注入 Blackboard，供节点通过占位符消费触发载荷。
    /// 仅当 Context 为合法 JSON 且含 `trigger` 对象时生效；否则原样返回 Blackboard（遗留工作流无影响）。
    /// </summary>
    private static Blackboard SeedTriggerBlackboard(Workflow workflow, Blackboard blackboard)
    {
        if (string.IsNullOrWhiteSpace(workflow.Context)) return blackboard;
        try
        {
            using var doc = JsonDocument.Parse(workflow.Context);
            if (!doc.RootElement.TryGetProperty("trigger", out var trigger) || trigger.ValueKind != JsonValueKind.Object)
                return blackboard;

            // 整个 trigger 信封作为 `trigger` 键（完整 JSON）。
            blackboard = blackboard.Set("trigger", trigger.GetRawText());

            // 平铺 trigger 的标量属性为 `trigger.<prop>`（如 trigger.type / trigger.firedAt）。
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

    /// <summary>将 JSON 标量（string/number/bool）或一层嵌套标量对象压为字符串；数组或深层结构返回 null。</summary>
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
            case JsonValueKind.Object:
                // 平铺一层嵌套对象的标量属性（如 trigger.payload.text）。
                var sb = new System.Text.StringBuilder();
                foreach (var p in el.EnumerateObject())
                {
                    var v = FlattenScalar(p.Value);
                    if (v is not null) sb.Append($"{p.Name}={v}; ");
                }
                return sb.Length == 0 ? null : sb.ToString(0, sb.Length - 2);
            default:
                return null;
        }
    }
}
