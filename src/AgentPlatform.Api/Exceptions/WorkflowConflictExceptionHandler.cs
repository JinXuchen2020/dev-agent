using System.Net;
using AgentPlatform.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="WorkflowConflictException"/> to HTTP 409 Conflict (RFC 9457 ProblemDetails).
/// Registered via <c>builder.Services.AddExceptionHandler&lt;WorkflowConflictExceptionHandler&gt;()</c>
/// and invoked by the existing <c>app.UseExceptionHandler()</c> middleware.
/// </summary>
internal sealed class WorkflowConflictExceptionHandler : IExceptionHandler
{
    private readonly ILogger<WorkflowConflictExceptionHandler> _logger;

    public WorkflowConflictExceptionHandler(ILogger<WorkflowConflictExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not WorkflowConflictException conflict)
            return false;

        _logger.LogWarning("Workflow conflict rejected: {Message}", conflict.Message);

        httpContext.Response.StatusCode = (int)HttpStatusCode.Conflict;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = "Conflict",
                Status = (int)HttpStatusCode.Conflict,
                Detail = conflict.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
