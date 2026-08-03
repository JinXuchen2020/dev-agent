using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>查询某工作流的触发器配置（受租户过滤），含 Chat 绑定计数。</summary>
/// <param name="WorkflowId">工作流标识。</param>
/// <param name="TenantId">租户。</param>
public record GetWorkflowTriggersQuery(Guid WorkflowId, Guid TenantId) : IRequest<WorkflowTriggersResponse?>;

/// <summary>工作流触发器配置响应（对齐 §7.2 GET /triggers）。</summary>
public sealed record WorkflowTriggersResponse(
    WebhookTriggerView? Webhook,
    ScheduleTriggerView? Schedule,
    int ChatBindingCount);

/// <summary>Webhook 触发器视图（仅鉴权后可见令牌）。</summary>
public sealed record WebhookTriggerView(string? TriggerToken, bool Enabled);

/// <summary>Schedule 触发器视图（含预计算下次运行 UTC 时间）。</summary>
public sealed record ScheduleTriggerView(string? Cron, string? Timezone, bool Enabled, DateTime? NextRunAt);
