using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>为工作流创建或启用 Webhook 触发器令牌（幂等：已存在则复用现有令牌并确保启用、不轮换；不存在则新建）。</summary>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">工作流所属租户。</param>
public record GenerateWebhookTokenCommand(Guid WorkflowId, Guid TenantId)
    : IRequest<WebhookTokenResult?>;

/// <summary>Webhook 令牌结果。</summary>
public sealed record WebhookTokenResult(Guid TriggerId, string Token, bool Created);
