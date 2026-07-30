using AgentPlatform.Application.AgentRoleManagement.Commands.CreateAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Commands.DeleteAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Commands.UpdateAgentRoleDefinition;
using AgentPlatform.Application.AgentRoleManagement.Queries.GetAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Queries.ListAgentRoles;
using Microsoft.AspNetCore.Http;
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
    /// Updates an existing agent role definition (name / description / system prompt).
    /// The role code is the immutable key and cannot be changed.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{roleCode}")]
    public async Task<IActionResult> UpdateRole(
        string roleCode,
        [FromBody] UpdateAgentRoleRequest request,
        CancellationToken ct)
    {
        var command = new UpdateAgentRoleDefinitionCommand(
            roleCode,
            request.Name,
            request.Description,
            request.SystemPrompt);

        var result = await _mediator.Send(command, ct);
        if (result is null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Deletes an existing custom agent role by its role code.
    /// Built-in roles and roles still referenced by agents are rejected with 409 Conflict.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{roleCode}")]
    public async Task<IActionResult> DeleteRole(
        string roleCode,
        CancellationToken ct)
    {
        var outcome = await _mediator.Send(new DeleteAgentRoleCommand(roleCode), ct);
        return outcome switch
        {
            AgentRoleDeletionOutcome.Deleted => NoContent(),
            AgentRoleDeletionOutcome.NotFound => NotFound(),
            AgentRoleDeletionOutcome.BuiltInConflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "内置角色不可删除",
                Detail = "该角色为平台内置角色，不可删除。"
            }),
            _ => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "角色仍被引用",
                Detail = "该角色仍被至少一个 Agent 引用，请先解绑相关 Agent 后再删除。"
            }),
        };
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

/// <summary>
/// Request model for updating an agent role definition.
/// </summary>
public sealed record UpdateAgentRoleRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string Name,
    [StringLength(500)]
    string? Description,
    [Required]
    [StringLength(8000)]
    string SystemPrompt);

