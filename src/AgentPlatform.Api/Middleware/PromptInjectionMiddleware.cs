using System.Text;
using AgentPlatform.Infrastructure.Security;

namespace AgentPlatform.Api.Middleware;

/// <summary>
/// Middleware that sanitizes incoming request bodies for prompt injection patterns.
/// Scans JSON body content for known injection patterns and rejects dangerous input.
/// </summary>
internal sealed class PromptInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PromptInjectionMiddleware> _logger;

    /// <summary>
    /// Maximum request body size that can be inspected for injection patterns (100 KB).
    /// </summary>
    private const int MaxInspectableBodySize = 100_000;

    public PromptInjectionMiddleware(
        RequestDelegate next,
        ILogger<PromptInjectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only scan POST/PUT requests that might contain user messages
        if (context.Request.Method is "POST" or "PUT" &&
            context.Request.ContentType?.Contains("application/json") == true)
        {
            if (context.Request.ContentLength > MaxInspectableBodySize)
            {
                _logger.LogWarning("Request body too large ({Size} bytes), rejecting", 
                    context.Request.ContentLength);
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            // Read body and scan for injection patterns
            if (context.Request.ContentLength > 0)
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, 
                    detectEncodingFromByteOrderMarks: false, bufferSize: MaxInspectableBodySize, 
                    leaveOpen: true);
                var body = await reader.ReadToEndAsync();

                // Reset stream position so downstream handlers can read it
                context.Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    // Resolve scoped service at invocation time (middleware is singleton)
                    var sanitizer = context.RequestServices.GetRequiredService<PromptInjectionService>();
                    var sanitized = sanitizer.SanitizeUserMessage(body);
                    if (string.IsNullOrEmpty(sanitized) && sanitized != body)
                    {
                        // Sanitizer returned empty for a non-empty body → dangerous pattern detected
                        _logger.LogWarning("Prompt injection pattern detected in request body, rejecting");
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("Request rejected: potentially dangerous content detected.", 
                            Encoding.UTF8, context.RequestAborted);
                        return;
                    }
                }
            }
        }

        await _next(context);
    }
}
