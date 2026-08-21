using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Agents.Commands.CreateAgent;
using AgentPlatform.Application.Agents.Commands.DeleteAgent;
using AgentPlatform.Application.Agents.Commands.RunAgent;
using AgentPlatform.Application.Agents.Commands.UpdateAgent;
using AgentPlatform.Application.Agents.Queries.GetAgent;
using AgentPlatform.Application.Agents.Queries.GetAgents;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly ModelDefaults _defaults;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch commands and queries.</param>
    /// <param name="tenant">The tenant provider used to resolve the current tenant identifier.</param>
    /// <param name="defaultsOptions">The model default settings options used to fill missing request values.</param>
    public AgentsController(
        IMediator mediator,
        ITenantProvider tenant,
        IOptions<ModelDefaults> defaultsOptions)
    {
        _mediator = mediator;
        _tenant = tenant;
        _defaults = defaultsOptions.Value;
    }

    /// <summary>
    /// Creates a new agent using the provided request payload, applying configured model defaults
    /// for any optional fields that are not supplied.
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
        var command = new CreateAgentCommand(
            request.Name,
            request.RoleCode ?? "development",
            request.ModelProvider ?? _defaults.ModelProvider,
            request.ModelName ?? _defaults.ModelName,
            request.ModelApiUrl ?? _defaults.ModelApiUrl,
            request.SystemPrompt ?? _defaults.SystemPrompt,
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
        AgenticRunResult result;
        try
        {
            result = await _mediator.Send(new RunAgentGoalCommand(id, request.Goal), ct);
        }
        catch (InvalidOperationException)
        {
            // Agent not found (handler contract) → 404 rather than 500.
            return NotFound();
        }
        return Ok(new AgenticRunResponse(
            result.FinalAnswer,
            result.Iterations,
            result.TotalTokensIn,
            result.TotalTokensOut,
            result.Trace.Select(t => new AgenticTraceStepResponse(
                t.Iteration, t.ToolName, t.ArgumentsJson, t.Result, t.Success)).ToList()));
    }
}

