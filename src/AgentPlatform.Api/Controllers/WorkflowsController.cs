using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;
using AgentPlatform.Application.Workflows.Commands.RunWorkflow;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Application.Workflows.Queries.ListWorkflows;
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
            && (request.Steps is null || request.Steps.Count == 0))
        {
            return BadRequest("nothing to update");
        }

        var command = new UpdateWorkflowCommand(
            id,
            request.Name,
            request.InitialContext,
            request.Steps,
            _tenant.GetTenantId());
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
/// </summary>
public sealed record UpdateWorkflowRequest(
    string? Name = null,
    string? InitialContext = null,
    IReadOnlyList<string>? Steps = null);

/// <summary>
/// Request model for re-running an existing workflow.
/// </summary>
public sealed record RunExistingWorkflowRequest(
    OrchestrationPreset? Preset = null);

