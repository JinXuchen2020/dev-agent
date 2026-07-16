using AgentPlatform.Application.Workflows.Commands.RunWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Application.Workflows.Queries.ListWorkflows;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for managing and querying workflows.
/// All routes are prefixed with <c>api/v1/workflows</c>.
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch queries and commands.</param>
    public WorkflowsController(IMediator mediator)
    {
        _mediator = mediator;
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
    [HttpPost]
    public async Task<IActionResult> RunWorkflow(
        [FromBody] RunWorkflowRequest request,
        CancellationToken ct = default)
    {
        var command = new RunWorkflowCommand(
            request.Name,
            request.InitialContext,
            TenantId: Guid.Empty); // Phase 1: single tenant

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}

/// <summary>
/// Request model for creating and running a new workflow.
/// </summary>
public sealed record RunWorkflowRequest(
    string Name,
    string InitialContext);
