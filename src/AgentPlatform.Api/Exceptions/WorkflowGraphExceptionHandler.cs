using System.Net;
using AgentPlatform.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="WorkflowGraphException"/> to HTTP 422 Unprocessable Entity (RFC 9457 ProblemDetails).
/// Registered via <c>builder.Services.AddExceptionHandler&lt;WorkflowGraphExceptionHandler&gt;()</c>
/// and invoked by the existing <c>app.UseExceptionHandler()</c> middleware.
/// </summary>
internal sealed class WorkflowGraphExceptionHandler : IExceptionHandler
{
    private readonly ILogger<WorkflowGraphExceptionHandler> _logger;

    public WorkflowGraphExceptionHandler(ILogger<WorkflowGraphExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not WorkflowGraphException graph)
            return false;

        _logger.LogWarning("Workflow graph rejected: {Message}", graph.Message);

        httpContext.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = "Unprocessable Entity",
                Status = (int)HttpStatusCode.UnprocessableEntity,
                Detail = graph.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
