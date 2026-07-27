using AgentPlatform.Api.Models;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Research;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// Research Agent endpoint. Accepts an open question and streams multi-step research progress
/// (plan → searches → synthesis → report) over Server-Sent Events.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/research")]
public sealed class ResearchController : ControllerBase
{
    private readonly IMediator _mediator;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Initializes a new instance of the <see cref="ResearchController"/>.</summary>
    /// <param name="mediator">The MediatR mediator used to dispatch the research command.</param>
    public ResearchController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Runs a research for the given question and streams progress events as SSE.
    /// The terminal <c>event: done</c> frame closes the stream.
    /// </summary>
    [HttpPost]
    public async Task PostResearch([FromBody] ResearchRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question, nameof(request.Question));

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var command = new ResearchCommand(
            request.Question, request.MaxSteps, request.ModelId, request.FocusInstructions);

        var events = await _mediator.Send(command, ct);
        try
        {
            await foreach (var evt in events.WithCancellation(ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("event: done\ndata: {}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — graceful close.
        }
    }
}
