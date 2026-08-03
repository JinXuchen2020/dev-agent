using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Application.PublishedWorkflows.Commands.RunPublishedWorkflow;
using AgentPlatform.Application.PublishedWorkflows.Queries.ListMcpTools;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// 平台内 MCP 暴露端点（F22，v1 轻量形态，无独立进程/端口）。实现 JSON-RPC 2.0 的
/// <c>tools/list</c> 与 <c>tools/call</c>，把 <c>Enabled &amp;&amp; Mode==Mcp</c> 的已发布工作流
/// 暴露为 MCP tool。调用同样要求有效 API Key（复用 ApiKey scheme）并受 PerApiKey 限流。
/// </summary>
[Authorize(AuthenticationSchemes = "ApiKey")]
[EnableRateLimiting("PerApiKey")]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/mcp")]
public sealed class McpController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpController"/> class.
    /// </summary>
    public McpController(IMediator mediator, ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// JSON-RPC 2.0 入口：根据 <c>method</c> 分发到 tools/list 或 tools/call。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Handle(
        [FromBody] JsonElement request,
        CancellationToken ct = default)
    {
        object? id = ExtractId(request);

        if (!request.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
            return JsonRpcError(id, -32600, "Invalid Request: missing 'method'.");

        var method = methodEl.GetString();
        var paramsEl = request.TryGetProperty("params", out var p) ? p : default;

        return method switch
        {
            "tools/list" => await ToolsListAsync(id, ct),
            "tools/call" => await ToolsCallAsync(id, paramsEl, ct),
            _ => JsonRpcError(id, -32601, $"Method not found: {method}."),
        };
    }

    private async Task<IActionResult> ToolsListAsync(object? id, CancellationToken ct)
    {
        var tools = await _mediator.Send(new ListMcpToolsQuery(_tenant.GetTenantId()), ct);
        var result = new
        {
            tools = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = ParseSchema(t.InputSchema),
            }).ToArray(),
        };
        return JsonRpcResult(id, result);
    }

    private async Task<IActionResult> ToolsCallAsync(object? id, JsonElement paramsEl, CancellationToken ct)
    {
        if (paramsEl.ValueKind != JsonValueKind.Object)
            return JsonRpcError(id, -32602, "Invalid params: expected object with 'name' and 'arguments'.");
        if (!paramsEl.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return JsonRpcError(id, -32602, "Invalid params: missing 'name'.");

        var name = nameEl.GetString()!;
        var arguments = paramsEl.TryGetProperty("arguments", out var argsEl) ? argsEl : default;
        var inputJson = arguments.ValueKind == JsonValueKind.Undefined ? null : arguments.GetRawText();

        try
        {
            var response = await _mediator.Send(
                new RunPublishedWorkflowCommand(name, _tenant.GetTenantId(), inputJson, GetInvokingKeyId()), ct);

            if (response is null)
                return JsonRpcResult(id, new
                {
                    content = new[] { new { type = "text", text = "Tool not found or disabled." } },
                    isError = true,
                });

            return JsonRpcResult(id, new
            {
                content = new[] { new { type = "text", text = response.Output } },
                isError = response.Status == "Failed",
            });
        }
        catch (Exception ex)
        {
            // MCP 约定：工具执行错误以 result.isError=true 返回，而非 HTTP 错误。
            return JsonRpcResult(id, new
            {
                content = new[] { new { type = "text", text = ex.Message } },
                isError = true,
            });
        }
    }

    private static JsonElement ParseSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return JsonDocument.Parse("{}").RootElement.Clone();
        try
        {
            return JsonDocument.Parse(schemaJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static object? ExtractId(JsonElement request)
    {
        if (!request.TryGetProperty("id", out var idEl))
            return null;
        return idEl.ValueKind switch
        {
            JsonValueKind.String => idEl.GetString(),
            JsonValueKind.Number => idEl.TryGetInt64(out var l) ? l : idEl.GetDouble(),
            _ => null,
        };
    }

    private Guid? GetInvokingKeyId()
    {
        var claim = User.FindFirst("key_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private static IActionResult JsonRpcResult(object? id, object result) =>
        new OkObjectResult(new { jsonrpc = "2.0", id, result });

    private static IActionResult JsonRpcError(object? id, int code, string message) =>
        new OkObjectResult(new { jsonrpc = "2.0", id, error = new { code, message } });
}
