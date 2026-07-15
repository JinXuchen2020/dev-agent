namespace AgentPlatform.Api.Middleware;

/// <summary>
/// Middleware that propagates a correlation identifier through the request pipeline.
/// It reads an incoming <c>X-Correlation-Id</c> header (or generates a new one), stores it
/// in <see cref="HttpContext.Items"/> and <see cref="HttpContext.TraceIdentifier"/>, echoes it
/// back on the response, and adds it to the logging scope.
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationIdMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next delegate in the request pipeline.</param>
    /// <param name="logger">The logger used to add the correlation id to the logging scope.</param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, ensuring a correlation id is present and propagated
    /// to the response headers, trace identifier, and logging scope before invoking the next middleware.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous middleware pipeline execution.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();

        correlationId = correlationId.Length > 64 ? correlationId[..64] : correlationId;
        context.Items["CorrelationId"] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers.TryAdd("X-Correlation-Id", correlationId);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}
