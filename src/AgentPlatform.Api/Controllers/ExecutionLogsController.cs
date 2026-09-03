using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.ExecutionLogs.Commands.ReplayExecution;
using AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogDetail;
using AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogs;
using AgentPlatform.Application.ExecutionLogs.Queries.GetExecutionLogSteps;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for querying workflow execution logs.
/// All routes are prefixed with <c>api/v1/execution-logs</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/execution-logs")]
public sealed class ExecutionLogsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionLogsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch queries.</param>
    /// <param name="tenant">The tenant provider supplying the current tenant scope (F40 回放归属校验).</param>
    public ExecutionLogsController(IMediator mediator, ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// Retrieves a paginated list of execution logs with optional filters.
    /// </summary>
    /// <param name="status">Optional filter by workflow execution status.</param>
    /// <param name="from">Optional start of the date range (inclusive).</param>
    /// <param name="to">Optional end of the date range (inclusive).</param>
    /// <param name="skip">Number of records to skip (default: 0).</param>
    /// <param name="take">Number of records to take (default: 20, max: 100).</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>A paginated list of execution log summaries.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet]
    public async Task<IActionResult> GetExecutionLogs(
        [FromQuery] WorkflowState? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (take < 1 || take > 100)
            return BadRequest("take must be between 1 and 100.");

        var query = new GetExecutionLogsQuery(
            status,
            from,
            to,
            skip,
            take);

        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the full detail of an execution log including all step entries.
    /// </summary>
    /// <param name="id">The unique identifier of the execution log.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>The execution log detail with step entries.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExecutionLogDetail(
        Guid id,
        CancellationToken ct = default)
    {
        var query = new GetExecutionLogDetailQuery(id);
        var result = await _mediator.Send(query, ct);

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Retrieves the step entries for an execution log with optional status filter and pagination.
    /// </summary>
    /// <param name="id">The execution log identifier.</param>
    /// <param name="status">Optional filter by step status.</param>
    /// <param name="skip">Number of records to skip (default: 0).</param>
    /// <param name="take">Number of records to take (default: 50, max: 100).</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>A paginated list of step entries.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id:guid}/steps")]
    public async Task<IActionResult> GetExecutionLogSteps(
        Guid id,
        [FromQuery] WorkflowState? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (take < 1 || take > 100)
            return BadRequest("take must be between 1 and 100.");

        var query = new GetExecutionLogStepsQuery(id, status, skip, take);
        var result = await _mediator.Send(query, ct);

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// F40 异常回放诊断：从执行日志<b>只读重建</b>失败工作流的节点路径、失败上下文与末次
    /// Blackboard 快照（不重新执行任何步骤、不写任何状态）。
    /// 日志不存在或不属于当前租户 → 404（不暴露存在性）。
    /// </summary>
    /// <remarks>
    /// 能力边界（详见 features/f40-replay-diagnostics.md §3）：每节点真实入参未落库（报告以
    /// <c>inputInferred=true</c> 标注推断值）；F30 检查点仅保留<b>末次</b>快照，故不声称可回放
    /// 每一步的上下文。旧数据（F24 之前）缺 NodeType/tokens 时以 <c>dataGaps</c> 如实标注，
    /// 前端据此灰显，避免把「信息缺失」误读为「没有失败」。
    /// </remarks>
    /// <param name="id">执行日志标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>回放报告（<c>ReplayReport</c>）。</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/replay")]
    public async Task<IActionResult> ReplayExecution(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ReplayExecutionCommand(id, _tenant.GetTenantId()), ct);
        return result == null ? NotFound() : Ok(result);
    }
}

