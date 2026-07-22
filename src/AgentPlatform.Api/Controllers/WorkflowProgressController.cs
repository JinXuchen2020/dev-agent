using AgentPlatform.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AgentPlatform.Api.Controllers;

/// <summary>
/// SSE endpoint for real-time workflow execution progress streaming.
/// Client connects via EventSource /api/v1/workflows/{id}/progress.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v1/workflows")]
public sealed class WorkflowProgressController : ControllerBase
{
    private readonly IExecutionProgressBroadcaster _broadcaster;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowProgressController"/> class.
    /// </summary>
    /// <param name="broadcaster">The progress broadcaster for subscribing to workflow events.</param>
    public WorkflowProgressController(IExecutionProgressBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    /// <summary>
    /// Opens an SSE stream that emits workflow execution progress events in real time.
    /// The stream stays open until the client disconnects or the workflow completes.
    /// </summary>
    /// <param name="id">The workflow identifier to subscribe to.</param>
    /// <param name="ct">A token to observe for cancellation of the request.</param>
    /// <returns>An SSE stream of <see cref="ExecutionProgressEvent"/> objects.</returns>
    [Authorize(Roles = "Admin,Operator")]
    [HttpGet("{id:guid}/progress")]
    public async Task StreamProgress(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            HttpContext.Response.StatusCode = 400;
            await HttpContext.Response.WriteAsync("Invalid workflow ID.", ct);
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (subscriberId, reader) = _broadcaster.Subscribe(id);

        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);

                // Close the stream on terminal events
                if (evt.Type is "workflow_completed" or "workflow_rolledback")
                {
                    await Response.WriteAsync("event: done\ndata: {}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected �?graceful cleanup
        }
        finally
        {
            // Always clean up subscriber channel to prevent memory leak
            _broadcaster.Unsubscribe(id, subscriberId);
        }
    }
}

