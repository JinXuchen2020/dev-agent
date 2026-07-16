using System.Diagnostics;
using AgentPlatform.Api.Diagnostics;

namespace AgentPlatform.Api.Middleware;

/// <summary>
/// Middleware that records OpenTelemetry metrics (request count, error count, duration)
/// for every API request passing through the pipeline.
/// </summary>
public sealed class MetricsMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricsMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public MetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware, recording metrics for the current request.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? "unknown";

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var statusCode = context.Response.StatusCode;

            DiagnosticsConfig.ApiRequestCounter.Add(1,
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("method", context.Request.Method),
                new KeyValuePair<string, object?>("status_code", statusCode));

            DiagnosticsConfig.ApiRequestDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("method", context.Request.Method));

            if (statusCode >= 400)
            {
                DiagnosticsConfig.ApiErrorCounter.Add(1,
                    new KeyValuePair<string, object?>("path", path),
                    new KeyValuePair<string, object?>("status_code", statusCode));
            }
        }
    }
}
