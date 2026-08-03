using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Application.PublishedWorkflows.Commands.RunPublishedWorkflow;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 已发布工作流的外部调用端点（F22，API 模式）。每条发布记录拥有一个公开 <c>slug</c>，
/// 调用方须持有效 API Key（复用现有 <c>ApiKeyAuthenticationHandler</c>，scheme="ApiKey"）。
/// 租户由密钥的 <c>tenant_id</c> 声明自动解析（<see cref="ITenantProvider"/>），
/// <c>key_id</c> 声明用于调用审计归属。限流复用 PerApiKey 令牌桶策略。
/// </summary>
[Authorize(AuthenticationSchemes = "ApiKey")]
[EnableRateLimiting("PerApiKey")]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/published-workflows")]
public sealed class PublishedWorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublishedWorkflowsController"/> class.
    /// </summary>
    public PublishedWorkflowsController(IMediator mediator, ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// 按 slug 运行已发布的 API 模式工作流。输入为可选 JSON 对象字符串（若定义了 InputSchema 则校验必填字段）。
    /// 无效 / 禁用 / 绑定 Key 不匹配的 slug → 404（不泄露存在性）。
    /// </summary>
    [HttpPost("{slug}")]
    public async Task<IActionResult> RunPublishedWorkflow(
        string slug,
        [FromBody] RunPublishedWorkflowRequest? request,
        CancellationToken ct = default)
    {
        var command = new RunPublishedWorkflowCommand(
            slug,
            _tenant.GetTenantId(),
            request?.InputJson,
            GetInvokingKeyId());

        var result = await _mediator.Send(command, ct);
        if (result is null)
            return NotFound();

        return Ok(result);
    }

    private Guid? GetInvokingKeyId()
    {
        var claim = User.FindFirst("key_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

/// <summary>
/// 请求模型：按 slug 运行已发布工作流（F22）。<see cref="InputJson"/> 为可选 JSON 对象字符串。
/// </summary>
public sealed record RunPublishedWorkflowRequest(string? InputJson = null);
