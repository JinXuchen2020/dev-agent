using AgentPlatform.Application.AgentRoleManagement.Commands.CreateAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Commands.DeleteAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Queries.GetAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Queries.ListAgentRoles;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for managing custom agent role definitions.
/// All routes are prefixed with <c>api/v1/agent-roles</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/agent-roles")]
public sealed class AgentRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRolesController"/> class.
    /// </summary>
    public AgentRolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a new custom agent role definition.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateAgentRoleRequest request,
        CancellationToken ct)
    {
        var command = new CreateAgentRoleCommand(
            request.Name,
            request.RoleCode,
            request.Description ?? string.Empty,
            request.SystemPrompt);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Deletes an existing custom agent role by its role code.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{roleCode}")]
    public async Task<IActionResult> DeleteRole(
        string roleCode,
        CancellationToken ct)
    {
        var command = new DeleteAgentRoleCommand(roleCode);
        var result = await _mediator.Send(command, ct);
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Lists all custom agent role definitions.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListRoles(CancellationToken ct)
    {
        var query = new ListAgentRolesQuery();
        var results = await _mediator.Send(query, ct);
        return Ok(results);
    }

    /// <summary>
    /// Retrieves a custom agent role by its role code.
    /// </summary>
    [HttpGet("{roleCode}")]
    public async Task<IActionResult> GetRole(
        string roleCode,
        CancellationToken ct)
    {
        var query = new GetAgentRoleQuery(roleCode);
        var result = await _mediator.Send(query, ct);
        if (result == null)
            return NotFound();
        return Ok(result);
    }
}

/// <summary>
/// Request model for creating a custom agent role.
/// </summary>
public sealed record CreateAgentRoleRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string Name,
    [Required]
    [StringLength(100, MinimumLength = 1)]
    string RoleCode,
    [StringLength(500)]
    string? Description,
    [Required]
    [StringLength(8000)]
    string SystemPrompt);

