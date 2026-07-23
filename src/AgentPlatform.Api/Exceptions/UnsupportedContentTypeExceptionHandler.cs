using System.Net;
using AgentPlatform.Api;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="UnsupportedContentTypeException"/> to HTTP 415 Unsupported Media Type (RFC 9457 ProblemDetails).
/// Registered via <c>builder.Services.AddExceptionHandler&lt;UnsupportedContentTypeExceptionHandler&gt;()</c>
/// and invoked by the existing <c>app.UseExceptionHandler()</c> middleware.
/// </summary>
internal sealed class UnsupportedContentTypeExceptionHandler : IExceptionHandler
{
    private readonly ILogger<UnsupportedContentTypeExceptionHandler> _logger;

    public UnsupportedContentTypeExceptionHandler(ILogger<UnsupportedContentTypeExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UnsupportedContentTypeException unsupported)
            return false;

        _logger.LogWarning("Unsupported content type rejected: {Message}", unsupported.Message);

        httpContext.Response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = "Unsupported Media Type",
                Status = (int)HttpStatusCode.UnsupportedMediaType,
                Detail = unsupported.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
