using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>为工作流启用/更新/禁用 Schedule 触发器（幂等 upsert）。</summary>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">工作流所属租户。</param>
/// <param name="Cron">5 段 cron 表达式（分 时 日 月 周）。</param>
/// <param name="Timezone">IANA 时区标识（如 "UTC"、"Asia/Shanghai"）。</param>
/// <param name="Enabled">是否启用。</param>
public record PutScheduleTriggerCommand(
    Guid WorkflowId, Guid TenantId, string Cron, string Timezone, bool Enabled)
    : IRequest<ScheduleTriggerResult?>;

/// <summary>Schedule 触发器结果（含下次运行 UTC 时间）。</summary>
public sealed record ScheduleTriggerResult(
    Guid TriggerId, string Cron, string Timezone, bool Enabled, DateTime? NextRunAt);
