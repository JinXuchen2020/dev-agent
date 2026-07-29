using System.Net;
using AgentPlatform.Api;
using AgentPlatform.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Api.Exceptions;

/// <summary>
/// Maps <see cref="Application.InvalidYamlException"/> to HTTP 400 Bad Request (RFC 9457 ProblemDetails).
/// Registered via <c>builder.Services.AddExceptionHandler&lt;InvalidYamlExceptionHandler&gt;()</c>
/// and invoked by the existing <c>app.UseExceptionHandler()</c> middleware.
/// </summary>
internal sealed class InvalidYamlExceptionHandler : IExceptionHandler
{
    private readonly ILogger<InvalidYamlExceptionHandler> _logger;

    public InvalidYamlExceptionHandler(ILogger<InvalidYamlExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not InvalidYamlException invalid)
            return false;

        _logger.LogWarning("Invalid YAML content rejected: {Message}", invalid.Message);

        httpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        await httpContext.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "about:blank",
                Title = "Invalid YAML",
                Status = (int)HttpStatusCode.BadRequest,
                Detail = invalid.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
