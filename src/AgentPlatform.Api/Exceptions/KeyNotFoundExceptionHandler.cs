using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="System.Collections.Generic.KeyNotFoundException"/> to HTTP 404 Not Found
/// (RFC 9457 ProblemDetails). Used by entity lookups (e.g. evaluation datasets) so a missing
/// resource returns a proper 404 instead of a 500. Registered via
/// <c>builder.Services.AddExceptionHandler&lt;KeyNotFoundExceptionHandler&gt;()</c>.
/// </summary>
internal sealed class KeyNotFoundExceptionHandler : IExceptionHandler
{
    private readonly ILogger<KeyNotFoundExceptionHandler> _logger;

    public KeyNotFoundExceptionHandler(ILogger<KeyNotFoundExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not KeyNotFoundException notFound)
            return false;

        _logger.LogWarning("Resource not found: {Message}", notFound.Message);

        httpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = "Not Found",
                Status = (int)HttpStatusCode.NotFound,
                Detail = notFound.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
