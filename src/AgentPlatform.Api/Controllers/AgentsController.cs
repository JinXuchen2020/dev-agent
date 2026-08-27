using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentRuns;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Agents.Commands.CreateAgent;
using AgentPlatform.Application.Agents.Commands.DeleteAgent;
using AgentPlatform.Application.Agents.Commands.RunAgent;
using AgentPlatform.Application.Agents.Commands.UpdateAgent;
using AgentPlatform.Application.Agents.Queries.GetAgent;
using AgentPlatform.Application.Agents.Queries.GetAgents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller exposing endpoints for creating and retrieving agents.
/// All routes are prefixed with <c>api/v1/agents</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;
    private readonly AgenticOrchestrator _orchestrator;
    private readonly IHostEnvironment _environment;
    private readonly IAgentRunRecorder _runRecorder;
    private readonly IPlatformModelProvider _platformModels;

    private static readonly JsonSerializerOptions SseOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentsController"/> class.
    /// </summary>
    public AgentsController(
        IMediator mediator,
        ITenantProvider tenant,
        AgenticOrchestrator orchestrator,
        IHostEnvironment environment,
        IAgentRunRecorder runRecorder,
        IPlatformModelProvider platformModels)
    {
        _mediator = mediator;
        _tenant = tenant;
        _orchestrator = orchestrator;
        _environment = environment;
        _runRecorder = runRecorder;
        _platformModels = platformModels;
    }

    /// <summary>
    /// Creates a new agent using the provided request payload.
    /// Model configuration (ModelProvider, ModelName, ModelApiUrl) is optional at creation time:
    /// when omitted, the agent is created with the platform's default model pinned as its endpoint
    /// (highest-priority enabled entry in the DB-backed <c>PlatformModels</c> catalog), so it is
    /// immediately routable. Runtime routing still prefers the tenant's BYO credentials when present
    /// — consistent with "all provider config lives in the DB".
    /// </summary>
    /// <param name="request">The request payload describing the agent to create.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the created agent as an <see cref="AgentResponse"/>.</returns>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateAgent(
        [FromBody] CreateAgentRequest request,
        CancellationToken ct)
    {
        // Model configuration is optional. When omitted, fall back to the platform's default
        // model (highest-priority enabled entry in the DB-backed PlatformModels catalog) so the
        // agent is created with a concrete, routable endpoint.
        var platformDefault = _platformModels.GetCandidates().FirstOrDefault();
        var provider = request.ModelProvider ?? platformDefault?.Provider ?? string.Empty;
        var modelName = request.ModelName ?? platformDefault?.ModelId ?? string.Empty;
        var apiUrl = request.ModelApiUrl ?? string.Empty;

        var command = new CreateAgentCommand(
            request.Name,
            request.RoleCode ?? "development",
            provider,
            modelName,
            apiUrl,
            request.SystemPrompt ?? "You are a helpful AI assistant.",
            _tenant.GetTenantId(),
            AllowedToolNames: request.AllowedToolNames,
            MaxIterations: request.MaxIterations,
            StopCriteria: request.StopCriteria);

        var agent = await _mediator.Send(command, ct);
        return Ok(AgentResponse.From(agent));
    }

    /// <summary>
    /// Retrieves an agent by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the agent to retrieve.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the agent as an <see cref="AgentResponse"/>; <c>404 Not Found</c> when the agent does not exist.</returns>
    [Authorize(Roles = "Admin,Operator,Viewer")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAgent(Guid id, CancellationToken ct)
    {
        var agent = await _mediator.Send(new GetAgentQuery(id), ct);
        if (agent == null) return NotFound();
        return Ok(AgentResponse.From(agent));
    }

    /// <summary>
    /// Retrieves all agents belonging to the current tenant.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing a list of agents as <see cref="AgentResponse"/> objects.</returns>
    [Authorize(Roles = "Admin,Operator,Viewer")]
    [HttpGet]
    public async Task<IActionResult> GetAgents(CancellationToken ct)
    {
        var agents = await _mediator.Send(new GetAgentsQuery(), ct);
        var responses = agents.Select(AgentResponse.From);
        return Ok(responses);
    }

    /// <summary>
    /// Updates an existing agent. Only supplied (non-null) fields are applied.
    /// </summary>
    /// <param name="id">The unique identifier of the agent to update.</param>
    /// <param name="request">The fields to update.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] UpdateAgentRequest request, CancellationToken ct)
    {
        var agent = await _mediator.Send(new UpdateAgentCommand(
            id,
            request.Name,
            request.RoleCode,
            request.ModelProvider,
            request.ModelName,
            request.ModelApiUrl,
            request.SystemPrompt,
            request.Status,
            request.AllowedToolNames,
            request.MaxIterations,
            request.StopCriteria), ct);

        if (agent is null) return NotFound();
        return Ok(AgentResponse.From(agent));
    }

    /// <summary>
    /// Deletes an agent by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the agent to delete.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAgent(Guid id, CancellationToken ct)
    {
        var deleted = await _mediator.Send(new DeleteAgentCommand(id), ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Runs an autonomous agentic control loop for the agent against the supplied goal and returns
    /// the final answer plus a per-step trace of tool calls and their results.
    /// </summary>
    /// <param name="id">The unique identifier of the agent to drive.</param>
    /// <param name="request">The request payload containing the goal.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id}/runs")]
    public async Task<IActionResult> RunAgent(Guid id, [FromBody] RunAgentGoalRequest request, CancellationToken ct)
    {
        var agent = await _mediator.Send(new GetAgentQuery(id), ct);
        if (agent is null) return NotFound();

        var runId = Guid.NewGuid();
        var startedAt = DateTime.UtcNow;
        AgenticRunResult result;
        try
        {
            result = await _mediator.Send(new RunAgentGoalCommand(id, request.Goal, runId), ct);
        }
        catch (InvalidOperationException)
        {
            // Agent not found (handler contract) → 404 rather than 500.
            return NotFound();
        }

        var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        await _runRecorder.RecordAsync(
            agent.TenantId, agent.Id, agent.Name, runId, request.Goal,
            AgentRunStatus.Completed, duration,
            result.Iterations, result.TotalTokensIn, result.TotalTokensOut,
            result.Artifacts?.Count ?? 0,
            Truncate(result.FinalAnswer, 20000), null, ct);

        return Ok(new AgenticRunResponse(
            result.FinalAnswer,
            result.Iterations,
            result.TotalTokensIn,
            result.TotalTokensOut,
            result.Trace.Select(t => new AgenticTraceStepResponse(
                t.Iteration, t.ToolName, t.ArgumentsJson, t.Result, t.Success)).ToList(),
            runId,
            result.Artifacts));
    }

    /// <summary>
    /// Runs an autonomous agentic control loop and streams progress as Server-Sent Events so the
    /// UI can render the thinking process and final answer in real time. Each event is a JSON object
    /// on a <c>data:</c> line. Event types: <c>iteration</c> / <c>tool_call</c> / <c>tool_result</c> /
    /// <c>answer_delta</c> / <c>done</c> / <c>error</c>.
    /// </summary>
    /// <param name="id">The unique identifier of the agent to drive.</param>
    /// <param name="request">The request payload containing the goal.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id}/runs/stream")]
    public async Task StreamRunAgent(Guid id, [FromBody] RunAgentGoalRequest request, CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var agent = await _mediator.Send(new GetAgentQuery(id), ct);
        if (agent is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var startedAt = DateTime.UtcNow;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        // 禁止中间代理（nginx/IIS/云 LB）缓冲 SSE，保证事件即时送达。
        Response.Headers["X-Accel-Buffering"] = "no";

        // 长任务（工具执行 / 模型调用）期间 SSE 长时间零字节，中间代理可能把连接判定为空闲
        // 而静默切断；15s 心跳注释行保持连接活性，也让前端能区分"仍在运行"与"已卡死"。
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = KeepAliveAsync(Response, TimeSpan.FromSeconds(15), heartbeatCts.Token);

        Exception? runError = null;
        bool cancelled = false;
        try
        {
            // 首帧告知前端本次 run 的 id，便于完成后拉取产物清单/预览。
            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new AgenticStreamEvent("run_start", RunId: runId.ToString()), SseOptions)}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            await foreach (var ev in _orchestrator.RunGoalStreamAsync(request.Goal, agent, runId, ct))
            {
                var json = JsonSerializer.Serialize(ev, SseOptions);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                if (ev.Type == "done")
                {
                    var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    await _runRecorder.RecordAsync(
                        agent.TenantId, agent.Id, agent.Name, runId, request.Goal,
                        AgentRunStatus.Completed, duration,
                        ev.Iteration ?? 0, ev.TokensIn ?? 0, ev.TokensOut ?? 0,
                        ev.Artifacts?.Count ?? 0,
                        Truncate(ev.FinalAnswer, 20000), null, ct);
                }
                else if (ev.Type == "error")
                {
                    runError = new Exception(ev.Error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端断开 / 用户主动停止 → 标记为取消，不写 error 事件。
            cancelled = true;
        }
        catch (Exception ex)
        {
            runError = ex;
            // 任何运行期异常都转为一条 error 事件，保证前端能展示失败原因而非连接中断。
            var errJson = JsonSerializer.Serialize(new AgenticStreamEvent("error", Error: ex.Message), SseOptions);
            try
            {
                await Response.WriteAsync($"data: {errJson}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) { /* 客户端已断开，忽略 */ }
        }
        finally
        {
            // 运行失败 / 取消也写入历史，便于回溯（用 None token，避免主请求取消波及历史写入）。
            if (runError is not null)
            {
                var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                await SafeRecordAsync(agent, runId, request.Goal, AgentRunStatus.Failed, duration,
                    0, 0, 0, 0, null, Truncate(runError.Message, 4000));
            }
            else if (cancelled)
            {
                var duration = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                await SafeRecordAsync(agent, runId, request.Goal, AgentRunStatus.Cancelled, duration,
                    0, 0, 0, 0, null, "用户取消或连接断开");
            }

            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { /* 心跳退出失败不阻断 */ }
        }
    }

    /// <summary>
    /// Lists the files generated by a previously completed agent run.
    /// </summary>
    /// <param name="id">The agent identifier (kept for route symmetry; artifacts are keyed by runId).</param>
    /// <param name="runId">The run identifier returned by the runs/stream <c>done</c> event or the runs response.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id}/runs/{runId}/artifacts")]
    public IActionResult GetRunArtifacts(Guid id, Guid runId, CancellationToken ct)
    {
        var root = ResolveArtifactRoot(runId);
        if (root is null || !Directory.Exists(root))
            return Ok(Array.Empty<object>());

        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => new
            {
                path = Path.GetRelativePath(root, f).Replace('\\', '/'),
                size = new FileInfo(f).Length,
                // 浏览器可直接在 iframe 中渲染/预览的类型标记。
                contentType = InferContentType(f)
            })
            .ToList();
        return Ok(entries);
    }

    /// <summary>
    /// Lists the run history for a single agent (tenant-scoped), newest first.
    /// </summary>
    /// <param name="id">The agent identifier.</param>
    /// <param name="page">1-based page index (default 1).</param>
    /// <param name="pageSize">Page size (default 20, max 100).</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id}/run-history")]
    public async Task<IActionResult> GetRunHistory(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var tenantId = _tenant.GetTenantId();
        var take = Math.Clamp(pageSize, 1, 100);
        var skip = Math.Max(0, page - 1) * take;

        var records = await _runRecorder.ListByAgentAsync(tenantId, id, skip, take, ct);
        var response = records.Select(r => new AgentRunHistoryResponse(
            r.RunId,
            r.AgentName,
            r.Goal,
            r.Status.ToString(),
            r.Iterations,
            r.TotalTokensIn,
            r.TotalTokensOut,
            r.ArtifactCount,
            r.DurationMs,
            r.FinalAnswer,
            r.ErrorMessage,
            r.CreatedAt)).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Downloads a single artifact file produced by a run. HTML is served as <c>text/html</c> so the
    /// UI can embed it in an iframe and run it in-place; other types get a content-disposition attachment.
    /// </summary>
    /// <param name="id">The agent identifier (route symmetry).</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="file">The artifact relative path (e.g. <c>index.html</c>), matching the list endpoint.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id}/runs/{runId}/artifacts/{*file}")]
    public IActionResult GetRunArtifact(Guid id, Guid runId, string file, CancellationToken ct)
    {
        var root = ResolveArtifactRoot(runId);
        if (root is null || !Directory.Exists(root))
            return NotFound();

        // 防路径逃逸：归一化后必须仍落在 run 根目录内。
        var full = Path.GetFullPath(Path.Combine(root, file ?? string.Empty));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid artifact path.");

        if (!System.IO.File.Exists(full))
            return NotFound();

        var contentType = InferContentType(full);
        if (contentType == "text/html")
            return PhysicalFile(full, "text/html; charset=utf-8");
        return PhysicalFile(full, contentType, Path.GetFileName(full));
    }

    private string? ResolveArtifactRoot(Guid runId)
    {
        var root = Path.Combine(_environment.ContentRootPath, "data", "agent-runs", runId.ToString("N"));
        return Directory.Exists(root) ? root : null;
    }

    private static string InferContentType(string file)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html",
            ".js" => "text/javascript",
            ".css" => "text/css",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null) return null;
        return value.Length <= max ? value : value.Substring(0, max);
    }

    /// <summary>
    /// Best-effort history write that never throws (a history persistence failure must not break the run response).
    /// </summary>
    private async Task SafeRecordAsync(
        Agent agent, Guid runId, string goal, AgentRunStatus status, long durationMs,
        int iterations, int tokensIn, int tokensOut, int artifactCount,
        string? finalAnswer, string? errorMessage)
    {
        try
        {
            await _runRecorder.RecordAsync(
                agent.TenantId, agent.Id, agent.Name, runId, goal,
                status, durationMs, iterations, tokensIn, tokensOut, artifactCount,
                finalAnswer, errorMessage, CancellationToken.None);
        }
        catch
        {
            // 历史写入失败不阻断主流程（SSE 已结束 / 已返回响应）。
        }
    }

    /// <summary>
    /// 周期性写入 SSE 注释行（合法的 keep-alive），保持长连接活性并刷新代理缓冲。
    /// </summary>
    private static async Task KeepAliveAsync(HttpResponse response, TimeSpan interval, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct);
                await response.WriteAsync(": keep-alive\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常结束（主循环完成 / 客户端断开）。
        }
        catch (Exception)
        {
            // 连接已断开，心跳静默退出，不干扰主流程。
        }
    }
}

