using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Conversations.Commands.CreateConversation;
using AgentPlatform.Application.Conversations.Commands.SendMessage;
using AgentPlatform.Application.Conversations.Queries.GetConversations;
using AgentPlatform.Application.Routing.Queries.GetCostReport;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// API controller exposing endpoints for managing conversations, sending messages, and retrieving cost reports.
/// All routes are prefixed with <c>api/v1/conversations</c>.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantProvider _tenant;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsController"/> class.
    /// </summary>
    /// <param name="mediator">The MediatR mediator used to dispatch commands and queries.</param>
    /// <param name="tenant">The tenant provider used to resolve the current tenant identifier.</param>
    public ConversationsController(
        IMediator mediator,
        ITenantProvider tenant)
    {
        _mediator = mediator;
        _tenant = tenant;
    }

    /// <summary>
    /// Creates a new conversation scoped to the current tenant.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the identifier of the newly created conversation.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost]
    public async Task<IActionResult> CreateConversation(CancellationToken ct)
    {
        var id = await _mediator.Send(new CreateConversationCommand(_tenant.GetTenantId()), ct);
        return Ok(new { id });
    }

    /// <summary>
    /// Retrieves all conversations belonging to the current tenant.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing a list of conversations.</returns>
    [HttpGet]
    public async Task<IActionResult> GetConversations(CancellationToken ct)
    {
        var conversations = await _mediator.Send(new GetConversationsQuery(), ct);
        return Ok(conversations);
    }

    /// <summary>
    /// Sends a message to an existing conversation and returns the agent's reply along with model and usage metadata.
    /// </summary>
    /// <param name="conversationId">The unique identifier of the conversation to send the message to.</param>
    /// <param name="request">The request payload containing the message content and optional overrides.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the reply, the model identifier, and token usage.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpPost("{conversationId}/messages")]
    public async Task<IActionResult> SendMessage(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var command = new SendMessageCommand(conversationId, request.Content, request.SearchQuery, request.Model);
        var result = await _mediator.Send(command, ct);
        return Ok(new SendMessageResponse(result.Reply, result.ModelId, result.TokenUsage));
    }

    /// <summary>
    /// Retrieves an aggregated cost report across conversations and model usage.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An <see cref="IActionResult"/> containing the cost report data.</returns>
    [Authorize(Roles = "Admin")]
    [HttpGet("cost-report")]
    public async Task<IActionResult> GetCostReport(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetCostReportQuery(), ct);
        return Ok(result);
    }
}
