using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.WorkflowTemplates;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for the platform-level workflow template market (F23).
/// Templates are shared across all tenants and readable by any authenticated user; cloning
/// (which creates a tenant-owned workflow) requires Admin / Operator.
/// All routes are prefixed with <c>api/v1/workflow-templates</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/workflow-templates")]
public sealed class WorkflowTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowTemplatesController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch queries and commands.</param>
    /// <param name="tenant">The tenant provider used to resolve the current tenant identifier.</param>
    public WorkflowTemplatesController(IMediator mediator, ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// Lists platform templates with optional category + keyword filtering.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListTemplates(
        [FromQuery] WorkflowTemplateCategory? category,
        [FromQuery] string? keyword,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ListWorkflowTemplatesQuery(category, keyword), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns all template categories for the market filter / dropdown.
    /// </summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetWorkflowTemplateCategoriesQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a single template with its preview graph (nodes / edges).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetWorkflowTemplateQuery(id), ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Clones a platform template into a new workflow owned by the caller's tenant.
    /// Requires Admin / Operator (same right as creating a workflow).
    /// </summary>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{id:guid}/clone")]
    public async Task<IActionResult> CloneTemplate(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new CloneWorkflowTemplateCommand(id, _tenant.GetTenantId()), ct);
        if (result is null)
            return NotFound();
        return Ok(result);
    }
}
