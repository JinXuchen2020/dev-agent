using System.Threading.RateLimiting;
using AgentPlatform.Api.Diagnostics;
using OpenTelemetry.Metrics;

namespace AgentPlatform.Api.Configuration;

/// <summary>
/// Configures cross-cutting infrastructure services: CORS, rate limiting,
/// and OpenTelemetry metrics/tracing.
/// </summary>
internal static class InfrastructureConfiguration
{
    public static IServiceCollection AddInfrastructureConfiguration(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            // Cookie-based auth requires explicit origins + AllowCredentials.
            // AllowAnyOrigin + AllowCredentials are mutually exclusive, so a dev
            // default (the Vite dev server) is used when no origins are configured.
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (origins is not { Length: > 0 })
                origins = new[] { "http://localhost:5173", "https://localhost:5173" };
            options.AddDefaultPolicy(p => p
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
        });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("PerTenant", context =>
            {
                var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(
                    tenantId, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 100,
                        TokensPerPeriod = 100,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.AddPolicy("PerApiKey", context =>
            {
                var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault() ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(
                    apiKey, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 50,
                        TokensPerPeriod = 50,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            // F21 匿名 Webhook 端点限流：按 token（路径）分区，缺失时回退到客户端 IP，防止令牌泄露后被滥用。
            options.AddPolicy("WebhookAnonymous", context =>
            {
                var token = context.Request.RouteValues.TryGetValue("token", out var t) ? t?.ToString() : null;
                var partitionKey = token ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 20,
                        TokensPerPeriod = 20,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.RejectionStatusCode = 429;
        });

        // OpenTelemetry — metrics + tracing
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(DiagnosticsConfig.ServiceName)
                .AddMeter(AgentPlatform.Application.Diagnostics.WorkflowMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter());

        return services;
    }
}
