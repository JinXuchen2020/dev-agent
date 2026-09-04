using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Workflows;

/// <summary>
/// 队列模式 run 的共享编排（决策 D2=B）：入队 → 轮询等待终态（上限 <c>QueueWaitTimeoutSeconds</c>）
/// → 返回统一 <see cref="WorkflowRunResult"/>。直跑路径（QueueEnabled=false）不经过此类，零变化。
/// </summary>
internal static class QueuedRunSupport
{
    /// <summary>是否处于队列模式。</summary>
    public static bool IsQueueMode(DurableExecutionSettings settings) => settings.QueueEnabled;

    /// <summary>
    /// 入队并等待。调用方须已保证工作流落库（worker 在独立 scope 按 id 加载）。
    /// </summary>
    public static async Task<WorkflowRunResult> EnqueueAndWaitAsync(
        IExecutionQueue queue,
        IWorkflowRepository workflowRepository,
        DurableExecutionSettings settings,
        ExecutionJob job,
        ILogger logger,
        CancellationToken ct)
    {
        var enqueue = await queue.EnqueueAsync(job, ct);
        if (enqueue is EnqueueResult.RejectedQueueFull or EnqueueResult.RejectedBackendUnavailable)
        {
            logger.LogWarning(
                "Queue enqueue rejected for workflow {WorkflowId} ({Result}) — explicit failure, no silent drop",
                job.WorkflowId, enqueue);
            return new WorkflowRunResult(null, QueueDispatchStatus.Rejected, job.WorkflowId, WorkflowState.Pending);
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, settings.QueueWaitTimeoutSeconds));
        var interval = TimeSpan.FromSeconds(Math.Max(1, settings.QueuePollIntervalSeconds));
        Workflow? last = null;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);
            // 必须绕过追踪器读库：本请求 scope 已跟踪 Pending 实例，GetByIdAsync(FindAsync)
            // 恒返回陈旧内存副本，worker 在独立 scope 提交的终态将永远不可见（等待必然超时误返 202）。
            last = await workflowRepository.GetByIdFreshAsync(job.WorkflowId, ct);
            if (last is null)
            {
                continue;
            }

            // 终态或 Paused（等待人工干预）都算「可返回」——带聚合返回，Api 侧与直跑同构。
            if (last.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
            {
                return new WorkflowRunResult(last, QueueDispatchStatus.Completed, job.WorkflowId, last.CurrentState);
            }
        }

        logger.LogInformation(
            "Queue wait window ({Seconds}s) elapsed for workflow {WorkflowId}; returning 202 queued",
            settings.QueueWaitTimeoutSeconds, job.WorkflowId);
        return new WorkflowRunResult(null, QueueDispatchStatus.Queued, job.WorkflowId, last?.CurrentState ?? WorkflowState.Pending);
    }

    /// <summary>构造队列作业载荷（Preset 以 int 传输，对齐平台枚举 int 约定）。</summary>
    public static ExecutionJob BuildJob(
        Guid workflowId,
        Guid tenantId,
        Guid workspaceId,
        OrchestrationPreset preset,
        Guid? requestingUserId = null,
        TriggerType? triggerType = null,
        string? payloadJson = null) =>
        new(
            Guid.NewGuid(),
            workflowId,
            tenantId,
            workspaceId,
            (int)preset,
            triggerType.HasValue ? (int)triggerType.Value : null,
            payloadJson,
            requestingUserId,
            EnqueuedAt: DateTime.UtcNow);
}
