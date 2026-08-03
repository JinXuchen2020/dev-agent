using AgentPlatform.Application.WorkflowTriggers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 匿名 Webhook 入口：外部系统凭不可猜 token 调用，token 即鉴权。不依赖 cookie/JWT，
/// 受 <c>WebhookAnonymous</c> 限流策略保护（按 token/IP 分区，超限 429）。
/// </summary>
[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/webhooks/workflow/{token}")]
[EnableRateLimiting("WebhookAnonymous")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhooksController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch the webhook invocation command.</param>
    public WebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// 触发绑定到该 token 的工作流。请求体原样作为触发器载荷注入工作流 Context（Blackboard）。
    /// token 不存在或 Webhook 禁用 → 404（不暴露存在性）。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Invoke(
        string token,
        CancellationToken ct)
    {
        // 读取原始请求体（任意 JSON / 文本），交由 handler 决定载荷形态。
        string? body = null;
        if (Request.ContentLength > 0 && Request.Body.CanRead)
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync(ct);
        }

        var result = await _mediator.Send(new InvokeWebhookCommand(token, body), ct);
        if (result is null)
            return NotFound();

        return Ok(new
        {
            workflowId = result.WorkflowId,
            workflowName = result.WorkflowName,
            state = result.State.ToString()
        });
    }
}
