using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.EventHandlers;

/// <summary>
/// F33 episodic 写回：工作流到达终态（完成/回滚）时，把「结局 + 各步骤产出摘要」沉淀为
/// 语义记忆，供后续运行的 compaction 召回与跨会话经验复用。失败教训与成功经验同等价值。
/// </summary>
public sealed class SemanticMemoryWriteBackHandler
    : INotificationHandler<DomainEventNotification<WorkflowCompleted>>,
      INotificationHandler<DomainEventNotification<WorkflowRolledBack>>
{
    private readonly ISemanticMemoryService _memory;
    private readonly IWorkflowRepository _repository;
    private readonly SemanticMemorySettings _settings;
    private readonly ILogger<SemanticMemoryWriteBackHandler> _logger;

    /// <summary>
    /// Initializes the write-back handler.
    /// </summary>
    /// <param name="memory">The semantic memory service used to persist run experiences.</param>
    /// <param name="repository">Workflow repository for loading the run's step outputs.</param>
    /// <param name="settings">Semantic memory settings (enable switch etc.).</param>
    /// <param name="logger">Logger.</param>
    public SemanticMemoryWriteBackHandler(
        ISemanticMemoryService memory,
        IWorkflowRepository repository,
        Microsoft.Extensions.Options.IOptions<SemanticMemorySettings> settings,
        ILogger<SemanticMemoryWriteBackHandler> logger)
    {
        _memory = memory;
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>成功完结 → 经验写回。</summary>
    public Task Handle(DomainEventNotification<WorkflowCompleted> notification, CancellationToken ct) =>
        WriteBackAsync(notification.DomainEvent.WorkflowId, "completed", null, ct);

    /// <summary>回滚（失败教训）→ 同样写回。</summary>
    public Task Handle(DomainEventNotification<WorkflowRolledBack> notification, CancellationToken ct) =>
        WriteBackAsync(notification.DomainEvent.WorkflowId, "rolled_back",
            notification.DomainEvent.ErrorDetail, ct);

    private async Task WriteBackAsync(Guid workflowId, string outcome, string? errorDetail, CancellationToken ct)
    {
        if (!_settings.Enabled)
            return;

        try
        {
            var workflow = await _repository.GetByIdAsync(workflowId, ct);
            if (workflow is null)
                return; // 租户过滤下不可见，静默跳过

            var digest = BuildDigest(workflow, errorDetail);
            await _memory.RememberRunAsync(
                workflow.TenantId, workflow.Id, workflow.Name, outcome, digest, ct);

            _logger.LogInformation(
                "Semantic memory write-back for workflow {WorkflowId} ({Outcome}), digest {Length} chars",
                workflowId, outcome, digest.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 记忆写回绝不影响主流程结果
            _logger.LogWarning(ex, "Semantic memory write-back failed for workflow {WorkflowId}", workflowId);
        }
    }

    private static string BuildDigest(Domain.Aggregates.Workflows.Workflow workflow, string? errorDetail)
    {
        var lines = new List<string>();
        foreach (var step in workflow.Steps.Where(s => s.State == WorkflowState.Completed))
        {
            var output = string.IsNullOrEmpty(step.Result) ? "(empty)" : step.Result!;
            lines.Add($"- {step.StepName}: {Truncate(output, 300)}");
        }
        if (lines.Count == 0)
            lines.Add("- (no completed steps)");
        if (!string.IsNullOrEmpty(errorDetail))
            lines.Add($"error: {Truncate(errorDetail, 300)}");

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}