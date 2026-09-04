using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.Workflows.Commands.ExecuteQueuedWorkflow;

/// <summary>worker 消费一条队列投递的执行结局。</summary>
public enum QueuedRunOutcome
{
    /// <summary>执行完成（编排到达终态/暂停）。</summary>
    Executed,

    /// <summary>工作流不存在或租户不匹配（毒消息，worker ack + 记日志）。</summary>
    NotFound,

    /// <summary>重复投递（至少一次投递常态），worker 直接 ack 跳过：租约被其他实例持有 / 正在运行，
    /// 或人工运行作业消费时工作流已处终态/暂停（入队前恒 Pending 落库 ⇒ 终态必为已执行过的重投）。</summary>
    Duplicate,

    /// <summary>执行失败（可重试，worker 按 Attempt 决定重投或 dead-letter）。</summary>
    Failed,
}

/// <summary>
/// worker 侧执行一条 <see cref="ExecutionJob"/>（F37）：在消费 scope 中复现租户/工作空间上下文
/// （Override，先于 DbContext 构造）→ 校验租户归属 → 人工运行走 <see cref="IOrchestrationPrimitive.RunAsync"/>
/// （内部 F30 租约互斥）；触发投递复用 <see cref="TriggerWorkflowCommand"/>（FromQueue=true，防再入队回环），
/// 信封合并/审计语义零复制。
/// </summary>
public sealed record ExecuteQueuedWorkflowCommand(ExecutionJob Job)
    : ICommand<QueuedRunOutcome>;

internal sealed class ExecuteQueuedWorkflowCommandHandler(
    IWorkflowRepository workflowRepository,
    IOrchestrationPrimitive primitive,
    IAuditLogRepository auditLogRepository,
    ITenantContext tenantContext,
    IWorkspaceContext workspaceContext,
    IMediator mediator,
    ILogger<ExecuteQueuedWorkflowCommandHandler> logger)
    : IRequestHandler<ExecuteQueuedWorkflowCommand, QueuedRunOutcome>
{
    public async Task<QueuedRunOutcome> Handle(ExecuteQueuedWorkflowCommand request, CancellationToken ct)
    {
        var job = request.Job;

        // 消费 scope 无 HTTP 上下文：载荷复现执行环境（F35 工作空间 + 租户 Override）。
        tenantContext.OverrideTenantId = job.TenantId;
        workspaceContext.OverrideWorkspaceId = job.WorkspaceId == Guid.Empty ? null : job.WorkspaceId;

        var wf = await workflowRepository.GetByIdAsync(job.WorkflowId, ct);
        if (wf is null || wf.TenantId != job.TenantId)
        {
            // 跨租户/已删除：绝不代跑，ack 丢弃并告警（毒消息不重试）。
            logger.LogWarning(
                "Queued job {JobId} references workflow {WorkflowId} not resolvable in tenant {TenantId} — dropped",
                job.JobId, job.WorkflowId, job.TenantId);
            return QueuedRunOutcome.NotFound;
        }

        try
        {
            if (job.TriggerType is { } triggerType)
            {
                // 触发投递：复用既有触发命令（信封合并 + 审计），FromQueue=true 阻断再入队回环。
                var result = await mediator.Send(new TriggerWorkflowCommand(
                    job.WorkflowId, job.TenantId, (TriggerType)triggerType, job.PayloadJson, FromQueue: true), ct);
                return result is null ? QueuedRunOutcome.NotFound : QueuedRunOutcome.Executed;
            }

            // F37 审查修复（幂等）：人工运行作业在入队前恒已 Pending 落库（Run/RunExisting 队列路径先 SaveChanges），
            // 因此消费时见到终态/暂停 = 本作业（或同工作流其它作业）已执行过的重复投递（至少一次投递常态）。
            // 绝不允许 Reset 复位重跑——那会把已完成的工作流二次执行。
            if (wf.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
            {
                logger.LogInformation(
                    "Queued job {JobId} skipped: workflow {WorkflowId} already in terminal/paused state {State} " +
                    "(duplicate delivery after execution — not re-running)",
                    job.JobId, job.WorkflowId, wf.CurrentState);
                return QueuedRunOutcome.Duplicate;
            }

            // Running 不在此处预检跳过：由 F30 租约仲裁——活租约（另一 worker 在跑）→ RunAsync 抛
            // "already running on another instance" → 下方 catch 映射 Duplicate；
            // 过期租约（原 worker 崩溃）→ TryAcquireLease 成功接管，从检查点续跑（XCLAIM/XAUTOCLAIM 重投语义）。
            var preset = (OrchestrationPreset)job.Preset;
            var resultWf = await primitive.RunAsync(wf, preset, ct);

            var audit = AuditLog.Record(
                tenantId: resultWf.TenantId,
                action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RunWorkflow,
                entity: "Workflow",
                entityId: resultWf.Id,
                details: job.RequestingUserId is { } requester
                    ? $"Queued run executed (job {job.JobId}, attempt {job.Attempt}, requesting user {requester})"
                    : $"Queued run executed (job {job.JobId}, attempt {job.Attempt})");
            auditLogRepository.Add(audit);

            return QueuedRunOutcome.Executed;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already running on another instance", StringComparison.Ordinal))
        {
            // F30 租约被其他实例持有 = 重复投递（至少一次投递的常态），ack 跳过即可。
            logger.LogInformation(
                "Queued job {JobId}: lease held by another instance — duplicate delivery acked, workflow {WorkflowId} untouched",
                job.JobId, job.WorkflowId);
            return QueuedRunOutcome.Duplicate;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Queued job {JobId} failed for workflow {WorkflowId} (attempt {Attempt})",
                job.JobId, job.WorkflowId, job.Attempt);
            return QueuedRunOutcome.Failed;
        }
    }
}
