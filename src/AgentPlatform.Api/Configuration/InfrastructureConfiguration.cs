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
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (origins is { Length: > 0 })
                options.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
            else
                options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
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
