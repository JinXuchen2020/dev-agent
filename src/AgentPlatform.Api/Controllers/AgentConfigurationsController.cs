using AgentPlatform.Application.AgentConfigurationManagement;
using AgentPlatform.Application.AgentConfigurationManagement.Commands.CreateAgentConfiguration;
using AgentPlatform.Application.AgentConfigurationManagement.Commands.DeleteAgentConfiguration;
using AgentPlatform.Application.AgentConfigurationManagement.Commands.UpdateAgentConfiguration;
using AgentPlatform.Application.AgentConfigurationManagement.Queries.GetAgentConfiguration;
using AgentPlatform.Application.AgentConfigurationManagement.Queries.GetAgentConfigurationsByType;
using AgentPlatform.Application.AgentConfigurationManagement.Queries.GetConfigurationTemplate;
using AgentPlatform.Application.AgentConfigurationManagement.Queries.ListAgentConfigurations;
using AgentPlatform.Domain.Aggregates.AgentConfigurations;
using AgentPlatform.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller for managing agent configuration definitions.
/// All routes are prefixed with <c>api/v1/agent-configurations</c>.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/agent-configurations")]
public sealed class AgentConfigurationsController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentConfigurationsController"/> class.
    /// </summary>
    public AgentConfigurationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a new agent configuration.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> CreateConfiguration(
        [FromBody] CreateAgentConfigurationRequest request,
        CancellationToken ct)
    {
        var command = new CreateAgentConfigurationCommand(
            request.Name,
            request.YamlContent,
            request.Description,
            request.AgentTypeCode);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Updates an existing agent configuration.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateConfiguration(
        Guid id,
        [FromBody] UpdateAgentConfigurationRequest request,
        CancellationToken ct)
    {
        var command = new UpdateAgentConfigurationCommand(
            id,
            request.YamlContent,
            request.ChangeLog,
            request.VersionBump ?? VersionBump.Patch,
            request.Name,
            request.Description);

        var result = await _mediator.Send(command, ct);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Deletes an agent configuration by its unique identifier.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteConfiguration(
        Guid id,
        CancellationToken ct)
    {
        var command = new DeleteAgentConfigurationCommand(id);
        var result = await _mediator.Send(command, ct);
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Lists all agent configurations with optional status and pagination filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListConfigurations(
        [FromQuery] AgentConfigurationStatus? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (take < 1 || take > 100)
            return BadRequest("take must be between 1 and 100.");

        var query = new ListAgentConfigurationsQuery(status, skip, take);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves an agent configuration by its unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetConfiguration(
        Guid id,
        CancellationToken ct)
    {
        var query = new GetAgentConfigurationQuery(id);
        var result = await _mediator.Send(query, ct);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Retrieves all agent configurations associated with a specific agent type code.
    /// </summary>
    [HttpGet("by-type/{agentTypeCode}")]
    public async Task<IActionResult> GetConfigurationsByType(
        string agentTypeCode,
        CancellationToken ct)
    {
        var query = new GetAgentConfigurationsByTypeQuery(agentTypeCode);
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Projects an agent configuration into a structured, instantiation-ready template by
    /// parsing its YAML content on the server. Used by the "create agent from template" flow.
    /// Cross-tenant ids return <c>404 Not Found</c>; non-Admin callers receive <c>403 Forbidden</c>.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}/template")]
    public async Task<IActionResult> GetConfigurationTemplate(
        Guid id,
        CancellationToken ct)
    {
        var query = new GetConfigurationTemplateQuery(id);
        var result = await _mediator.Send(query, ct);
        if (result == null)
            return NotFound();
        return Ok(result);
    }
}

/// <summary>
/// Request model for creating a new agent configuration.
/// </summary>
public sealed record CreateAgentConfigurationRequest(
    [Required]
    [StringLength(200, MinimumLength = 1)]
    string Name,
    [Required]
    [StringLength(16000)]
    string YamlContent,
    [StringLength(1000)]
    string? Description,
    [StringLength(100)]
    string? AgentTypeCode);

/// <summary>
/// Request model for updating an existing agent configuration.
/// </summary>
public sealed record UpdateAgentConfigurationRequest(
    [Required]
    [StringLength(16000)]
    string YamlContent,
    [StringLength(2000)]
    string? ChangeLog,
    VersionBump? VersionBump,
    [StringLength(200)]
    string? Name,
    [StringLength(1000)]
    string? Description);

