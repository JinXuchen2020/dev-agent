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
        [FromBody] PublishWorkflowRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return BadRequest("Request body is required.");

        var command = new PublishWorkflowCommand(
            id, request.Mode, request.ApiKeyId, request.InputSchemaJson, _tenant.GetTenantId());
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

