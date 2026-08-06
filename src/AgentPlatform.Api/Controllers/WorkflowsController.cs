using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.ResolveApproval;
using AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;
using AgentPlatform.Application.Workflows.Commands.RunNode;
using AgentPlatform.Application.Workflows.Commands.RunWorkflow;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Application.Workflows.Queries.ListApprovals;
using AgentPlatform.Application.Workflows.Queries.ListWorkflows;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Application.Workflows.Commands.PublishWorkflow;
using AgentPlatform.Application.Workflows.Commands.UnpublishWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetPublishStatus;
using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Application.Workflows.Versioning.DiffWorkflow;
using AgentPlatform.Application.Debug.Commands.StartDebugSession;
using AgentPlatform.Application.Debug.Commands.ResetDebugSession;
using AgentPlatform.Application.Debug.Commands.DebugStep;
using AgentPlatform.Application.Debug.Commands.DebugResume;
using AgentPlatform.Application.Debug.Commands.DebugRetryNode;
using AgentPlatform.Application.Debug.Commands.DebugRollback;
using AgentPlatform.Application.Debug.Queries.GetDebugState;
using AgentPlatform.Application.Debug.Queries.GetDebugVariables;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for managing and querying workflows.
/// All routes are prefixed with <c>api/v1/workflows</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/workflows")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch queries and commands.</param>
    /// <param name="tenant">The tenant provider used to resolve the current tenant identifier.</param>
    public WorkflowsController(IMediator mediator, ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// Retrieves a paginated list of workflows with optional status filter.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListWorkflows(
        [FromQuery] WorkflowState? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (take < 1 || take > 100)
            return BadRequest("take must be between 1 and 100.");

        var query = new ListWorkflowsQuery(status, skip, take);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the full detail of a workflow by its ID, including all steps.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWorkflow(
        Guid id,
        CancellationToken ct = default)
    {
        var query = new GetWorkflowQuery(id);
        var result = await _mediator.Send(query, ct);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Creates and starts a new workflow with the specified name and initial context.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost]
    public async Task<IActionResult> RunWorkflow(
        [FromBody] RunWorkflowRequest request,
        CancellationToken ct = default)
    {
        var command = new RunWorkflowCommand(
            request.Name,
            request.InitialContext,
            TenantId: _tenant.GetTenantId(),
            Steps: request.Steps);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates a workflow draft without executing it (partial update). At least one of
    /// name / initialContext / steps must be supplied.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateWorkflow(
        Guid id,
        [FromBody] UpdateWorkflowRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            && string.IsNullOrWhiteSpace(request.InitialContext)
            && (request.Steps is null || request.Steps.Count == 0)
            && (request.Nodes is null || request.Nodes.Count == 0))
        {
            return BadRequest("nothing to update");
        }

        var command = new UpdateWorkflowCommand(
            id,
            request.Name,
            request.InitialContext,
            request.Steps,
            request.Nodes,
            request.Edges,
            _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Runs a single node of an existing workflow for debugging, without executing or
    /// completing the whole workflow. SSE/whole-run is unaffected. Returns the node's
    /// resulting state and captured output.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/nodes/{nodeId:guid}/run")]
    public async Task<IActionResult> RunNode(
        Guid id,
        Guid nodeId,
        CancellationToken ct = default)
    {
        var command = new RunNodeCommand(id, nodeId, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Re-runs an existing workflow by id, reusing the same aggregate (no duplicate created).
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> RunExistingWorkflow(
        Guid id,
        [FromBody] RunExistingWorkflowRequest? request,
        CancellationToken ct = default)
    {
        var command = new RunExistingWorkflowCommand(
            id,
            request?.Preset ?? OrchestrationPreset.Sequential,
            _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Snapshots the current definition of a workflow as a new version.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> CreateVersion(
        Guid id,
        [FromBody] CreateVersionRequest? request,
        CancellationToken ct = default)
    {
        var command = new CreateWorkflowVersionCommand(id, _tenant.GetTenantId(), request?.Note);
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Lists versions of a workflow ordered by version number descending.
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> ListVersions(
        Guid id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (take < 1 || take > 100)
            return BadRequest("take must be between 1 and 100.");

        var query = new ListWorkflowVersionsQuery(id, skip, take);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a single workflow version with its captured graph.
    /// </summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    public async Task<IActionResult> GetVersion(
        Guid id,
        Guid versionId,
        CancellationToken ct = default)
    {
        var query = new GetWorkflowVersionQuery(id, versionId);
        var result = await _mediator.Send(query, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Rolls a workflow back to a saved version.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
    public async Task<IActionResult> RestoreVersion(
        Guid id,
        Guid versionId,
        CancellationToken ct = default)
    {
        var command = new RestoreWorkflowVersionCommand(id, versionId, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Deletes a workflow version.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpDelete("{id:guid}/versions/{versionId:guid}")]
    public async Task<IActionResult> DeleteVersion(
        Guid id,
        Guid versionId,
        CancellationToken ct = default)
    {
        var command = new DeleteWorkflowVersionCommand(id, versionId, _tenant.GetTenantId());
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Exports the current definition of a workflow as JSON.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> ExportWorkflow(
        Guid id,
        CancellationToken ct = default)
    {
        var query = new ExportWorkflowQuery(id);
        var result = await _mediator.Send(query, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Imports a workflow definition as a new workflow.
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("import")]
    public async Task<IActionResult> ImportWorkflow(
        [FromBody] ImportWorkflowRequest? request,
        CancellationToken ct = default)
    {
        if (request is null)
            return BadRequest("Request body is required.");

        var command = new ImportWorkflowCommand(
            request.Name, request.InitialContext, request.Nodes, request.Edges, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 列出某工作流的全部人工审批门（HITL）记录（F20 S3）。租户隔离由查询处理保证。
    /// execId 在查询/解析中均无需（审批按 workflowId 归并、由 approvalId 唯一定位），
    /// 故路径仅取 {id}。
    /// </summary>
    [HttpGet("{id:guid}/approvals")]
    public async Task<IActionResult> ListApprovals(
        Guid id,
        CancellationToken ct = default)
    {
        var query = new ListApprovalsQuery(id);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// 解析（批准/拒绝）一个人工审批门（F20 S3）：加载审批（租户校验）→ 置 Approved/Rejected；
    /// 将对应 UserInput 节点结果写回并标记 Completed；续跑暂停的工作流（跳过已完成节点）。
    /// </summary>
    [HttpPost("{id:guid}/approvals/{approvalId:guid}/resolve")]
    public async Task<IActionResult> ResolveApproval(
        Guid id,
        Guid approvalId,
        [FromBody] ResolveApprovalRequest request,
        CancellationToken ct = default)
    {
        var command = new ResolveApprovalCommand(
            id,
            approvalId,
            request.Approved,
            request.Input,
            _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// 发布工作流为外部可执行能力（F22）：API 端点或 MCP tool。每工作流至多一条发布记录，重复发布替换既有。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> PublishWorkflow(
        Guid id,
        [FromBody] PublishWorkflowRequest dto,
        CancellationToken ct = default)
    {
        if (dto is null)
            return BadRequest("Request body is required.");

        var command = new PublishWorkflowCommand(
            id, dto.Mode, dto.ApiKeyId, dto.InputSchemaJson, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 取消发布工作流（F22）。幂等：未发布则无操作。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpDelete("{id:guid}/publish")]
    public async Task<IActionResult> UnpublishWorkflow(
        Guid id,
        CancellationToken ct = default)
    {
        await _mediator.Send(new UnpublishWorkflowCommand(id, _tenant.GetTenantId()), ct);
        return NoContent();
    }

    /// <summary>
    /// 查询某工作流的发布状态（F22）。未发布返回 204。
    /// </summary>
    [HttpGet("{id:guid}/publish")]
    public async Task<IActionResult> GetPublishStatus(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPublishStatusQuery(id, _tenant.GetTenantId()), ct);
        if (result is null)
            return NoContent();
        return Ok(result);
    }

    /// <summary>
    /// 为工作流生成/启用 Webhook 触发器令牌（幂等：已存在则复用现有令牌并确保启用）。
    /// 返回令牌供拼接回调 URL。仅 Admin/Operator。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/triggers/webhook")]
    public async Task<IActionResult> GenerateWebhookToken(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GenerateWebhookTokenCommand(id, _tenant.GetTenantId()), ct);
        if (result is null)
            return NotFound();
        return Ok(new { triggerToken = result.Token, created = result.Created });
    }

    /// <summary>
    /// 禁用某工作流的 Webhook 触发器：令牌保留但失效（匿名调用 → 404）。幂等。仅 Admin/Operator。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpDelete("{id:guid}/triggers/webhook")]
    public async Task<IActionResult> DisableWebhookTrigger(
        Guid id,
        CancellationToken ct = default)
    {
        await _mediator.Send(
            new DisableWebhookTriggerCommand(id, _tenant.GetTenantId()), ct);
        return Ok(new { enabled = false });
    }

    /// <summary>
    /// 启用/更新/禁用某工作流的 Schedule（cron）触发器（幂等 upsert）。仅 Admin/Operator。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPut("{id:guid}/triggers/schedule")]
    public async Task<IActionResult> PutScheduleTrigger(
        Guid id,
        [FromBody] PutScheduleTriggerRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Cron))
            return BadRequest("cron is required.");

        var result = await _mediator.Send(
            new PutScheduleTriggerCommand(
                id, _tenant.GetTenantId(), request.Cron,
                request.Timezone ?? "UTC", request.Enabled), ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 查询某工作流的触发器配置（Webhook/Schedule/Chat 绑定数）。仅鉴权用户可见。
    /// </summary>
    [HttpGet("{id:guid}/triggers")]
    public async Task<IActionResult> GetTriggers(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetWorkflowTriggersQuery(id, _tenant.GetTenantId()), ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // F25 · Workflow Debugger (变量监视 + 单步重跑 + 错误分支)
    // 写操作仅 Admin/Operator；读操作继承类级 [Authorize]。
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 启动（或重置）一个工作流的调试会话：重置所有节点为 Pending，并新建一条 DebugSession。
    /// 可选携带 initialContext 作为调试初始上下文。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/run")]
    public async Task<IActionResult> StartDebugSession(
        Guid id,
        [FromBody] StartDebugSessionRequest? request,
        CancellationToken ct = default)
    {
        var command = new StartDebugSessionCommand(
            id, _tenant.GetTenantId(), request?.InitialContext);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 单步执行：运行当前调试会话中下一个 Pending 节点，然后暂停并回写变量快照。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/step")]
    public async Task<IActionResult> DebugStep(
        Guid id,
        [FromBody] DebugSessionRequest request,
        CancellationToken ct = default)
    {
        var command = new DebugStepCommand(id, request.SessionId, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 续跑：从当前调试状态继续运行至完成（绕过人工审批门时的调试用）。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/resume")]
    public async Task<IActionResult> DebugResume(
        Guid id,
        [FromBody] DebugSessionRequest request,
        CancellationToken ct = default)
    {
        var command = new DebugResumeCommand(id, request.SessionId, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 单节点重跑：在调试会话中重新执行指定节点（可携带覆盖配置）。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/retry-node")]
    public async Task<IActionResult> DebugRetryNode(
        Guid id,
        [FromBody] DebugRetryNodeRequest request,
        CancellationToken ct = default)
    {
        var command = new DebugRetryNodeCommand(
            id, request.SessionId, request.NodeId, _tenant.GetTenantId(), request.OverriddenConfig);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 错误分支/精确回滚：将调试中的工作流回滚到指定步骤序（将后续节点置回 Pending）。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/rollback")]
    public async Task<IActionResult> DebugRollback(
        Guid id,
        [FromBody] DebugRollbackRequest request,
        CancellationToken ct = default)
    {
        var command = new DebugRollbackCommand(
            id, request.SessionId, request.TargetStepOrder, _tenant.GetTenantId());
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// 查询调试中工作流的当前执行状态快照（各节点状态/结果）。仅鉴权用户可见。
    /// </summary>
    [HttpGet("{id:guid}/debug/state")]
    public async Task<IActionResult> GetDebugState(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetDebugStateQuery(id, _tenant.GetTenantId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// 查询调试会话累积的黑板变量（变量监视）。仅鉴权用户可见。
    /// </summary>
    [HttpGet("{id:guid}/debug/variables")]
    public async Task<IActionResult> GetDebugVariables(
        Guid id,
        [FromQuery] Guid sessionId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetDebugVariablesQuery(sessionId, _tenant.GetTenantId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// 重置调试会话与工作流到干净初始态（复用启动逻辑）。
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/debug/reset")]
    public async Task<IActionResult> ResetDebugSession(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResetDebugSessionCommand(id, _tenant.GetTenantId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// 计算工作流定义的差异（F26）：当前图 vs 指定版本对（fromVersionId/toVersionId）或另一工作流当前图
    /// （otherWorkflowId）；两者均未提供时默认对比「当前图 vs 最新保存版本」。读操作继承类级 [Authorize]。
    /// </summary>
    [HttpPost("{id:guid}/diff")]
    public async Task<IActionResult> DiffWorkflow(
        Guid id,
        [FromBody] DiffWorkflowRequest? request,
        CancellationToken ct = default)
    {
        var query = new DiffWorkflowQuery(
            id,
            request?.FromVersionId,
            request?.ToVersionId,
            request?.OtherWorkflowId,
            _tenant.GetTenantId());
        var result = await _mediator.Send(query, ct);
        return result == null ? NotFound() : Ok(result);
    }
}

/// <summary>
/// Request model for creating and running a new workflow.
/// </summary>
public sealed record RunWorkflowRequest(
    string Name,
    string InitialContext,
    IReadOnlyList<string>? Steps = null);

/// <summary>
/// Request model for updating a workflow draft. All fields optional (partial update).
/// Supplying <see cref="Nodes"/> + <see cref="Edges"/> replaces the DAG; otherwise
/// <see cref="Steps"/> replaces the legacy linear chain.
/// </summary>
public sealed record UpdateWorkflowRequest(
    string? Name = null,
    string? InitialContext = null,
    IReadOnlyList<string>? Steps = null,
    IReadOnlyList<WorkflowNodeRequest>? Nodes = null,
    IReadOnlyList<WorkflowEdgeRequest>? Edges = null);

/// <summary>
/// Request model for re-running an existing workflow.
/// </summary>
public sealed record RunExistingWorkflowRequest(
    OrchestrationPreset? Preset = null);

/// <summary>
/// Request model for creating a workflow version snapshot.
/// </summary>
public sealed record CreateVersionRequest(string? Note = null);

/// <summary>
/// Request model for resolving a HITL approval gate (F20 S3).
/// <paramref name="Approved"/> selects approve (write <paramref name="Input"/> as the
/// human input) or reject (write <paramref name="Input"/> as the rejection reason).
/// </summary>
public sealed record ResolveApprovalRequest(
    bool Approved,
    string? Input = null);

/// <summary>
/// Request model for publishing a workflow as an external API/MCP endpoint (F22).
/// <see cref="Mode"/> 必填（Api / Mcp）；<see cref="ApiKeyId"/> 为可选绑定（null = 租户任意有效 Key）；
/// <see cref="InputSchemaJson"/> 为可选 JSON Schema 片段（运行时做轻量 required 校验）。
/// </summary>
public sealed record PublishWorkflowRequest(
    PublishMode Mode,
    Guid? ApiKeyId = null,
    string? InputSchemaJson = null);
/// <summary>
/// Request model for enabling/updating a Schedule (cron) trigger (F21).
/// </summary>
public sealed record PutScheduleTriggerRequest(
    string Cron,
    string? Timezone = null,
    bool Enabled = true);

/// <summary>
/// Request model for starting a debug session (F25). <see cref="InitialContext"/> 为可选的
/// 调试初始上下文；为空则沿用工作流既有 initialContext。
/// </summary>
public sealed record StartDebugSessionRequest(string? InitialContext = null);

/// <summary>
/// Request model referencing an active debug session (F25). 多数调试写操作都需要 <see cref="SessionId"/>。
/// </summary>
public sealed record DebugSessionRequest(Guid SessionId);

/// <summary>
/// Request model for re-running a single node in a debug session (F25).
/// <see cref="OverriddenConfig"/> 可选，覆盖该节点的运行配置后再重跑。
/// </summary>
public sealed record DebugRetryNodeRequest(
    Guid SessionId,
    Guid NodeId,
    string? OverriddenConfig = null);

/// <summary>
/// Request model for rolling back a debugged workflow to a target step order (F25).
/// </summary>
public sealed record DebugRollbackRequest(
    Guid SessionId,
    int TargetStepOrder);

/// <summary>
/// Request model for computing a workflow definition diff (F26). All members optional:
/// supply a version pair, or an <see cref="OtherWorkflowId"/>, or neither (defaults to
/// current graph vs latest saved version).
/// </summary>
public sealed record DiffWorkflowRequest(
    Guid? FromVersionId = null,
    Guid? ToVersionId = null,
    Guid? OtherWorkflowId = null);

