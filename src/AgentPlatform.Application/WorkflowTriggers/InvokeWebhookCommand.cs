using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>
/// 匿名 Webhook 调用：按不可猜 token 解析触发器（跨租户），校验类型与启用状态后委托
/// <see cref="TriggerWorkflowCommand"/> 启动编排。控制器负责限流与 [AllowAnonymous]。
/// </summary>
/// <param name="Token">Webhook 令牌。</param>
/// <param name="BodyJson">请求体 JSON（将作为触发器载荷注入 Context）。</param>
public record InvokeWebhookCommand(string Token, string? BodyJson)
    : IRequest<TriggerRunResult?>;
