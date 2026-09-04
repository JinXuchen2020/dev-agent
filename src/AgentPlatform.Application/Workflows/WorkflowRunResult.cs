using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Workflows;

/// <summary>run 命令在队列模式下的投递状态（决策 D2=B）。</summary>
public enum QueueDispatchStatus
{
    /// <summary>未走队列（<c>QueueEnabled=false</c>，默认）——既有请求内同步直跑语义。</summary>
    NotQueued,

    /// <summary>已入队并执行到终态/暂停（等待窗口内完成）。</summary>
    Completed,

    /// <summary>已入队但等待窗口内未到终态——Api 返回 202 queued（显式不假成功）。</summary>
    Queued,

    /// <summary>入队被拒（队列满 / 后端不可用）——Api 返回 503，绝不静默丢任务。</summary>
    Rejected,
}

/// <summary>
/// run 命令统一结果：直跑模式与队列共用。
/// <paramref name="Workflow"/> 非空 = 拿到聚合（直跑完成或队列等待内到达可返回态）。
/// </summary>
public sealed record WorkflowRunResult(
    Domain.Aggregates.Workflows.Workflow? Workflow,
    QueueDispatchStatus Dispatch,
    Guid WorkflowId,
    WorkflowState? State);
