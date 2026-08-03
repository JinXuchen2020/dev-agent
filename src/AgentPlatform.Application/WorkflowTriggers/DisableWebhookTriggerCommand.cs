using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>吊销某工作流的 Webhook 触发器（禁用令牌，使其不再可被匿名调用）。幂等。</summary>
/// <param name="WorkflowId">工作流标识。</param>
/// <param name="TenantId">租户。</param>
public record DisableWebhookTriggerCommand(Guid WorkflowId, Guid TenantId) : IRequest<bool>;
