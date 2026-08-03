using System.Net;
using AgentPlatform.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="PublishedWorkflowException"/> to its carried <see cref="HttpStatusCode"/>
/// as an RFC 9457 ProblemDetails response. Registered via
/// <c>builder.Services.AddExceptionHandler&lt;PublishedWorkflowExceptionHandler&gt;()</c> and invoked
/// by the existing <c>app.UseExceptionHandler()</c> middleware.
/// </summary>
internal sealed class PublishedWorkflowExceptionHandler : IExceptionHandler
{
    private readonly ILogger<PublishedWorkflowExceptionHandler> _logger;

    public PublishedWorkflowExceptionHandler(ILogger<PublishedWorkflowExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PublishedWorkflowException pwEx)
            return false;

        _logger.LogWarning(
            "Published workflow rejected ({StatusCode}): {Message}",
            pwEx.StatusCode, pwEx.Message);

        var status = (int)pwEx.StatusCode;
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = pwEx.StatusCode.ToString(),
                Status = status,
                Detail = pwEx.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
