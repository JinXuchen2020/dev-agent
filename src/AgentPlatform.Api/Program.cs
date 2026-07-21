using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AgentPlatform.Api.Diagnostics;
using AgentPlatform.Api.Middleware;
using AgentPlatform.Application;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure;
using AgentPlatform.Infrastructure.Auth;
using AgentPlatform.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AgentPlatform API",
        Version = "v1",
        Description = "Multi-agent development platform API for managing agents, conversations, workflows, and model routing."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);

    var appXmlFile = "AgentPlatform.Application.xml";
    var appXmlPath = Path.Combine(AppContext.BaseDirectory, appXmlFile);
    if (File.Exists(appXmlPath))
        options.IncludeXmlComments(appXmlPath);
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var securitySection = builder.Configuration.GetSection("Security");
var enforceAuth = securitySection.GetValue<bool>("EnforceAuthentication");

builder.Services.AddAuthentication()
    .AddJwtBearer("Bearer", options =>
    {
        var jwtKey = securitySection["JwtSecretKey"] ?? "dev-secret-key-min-32-chars-long!!";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = securitySection["JwtIssuer"] ?? "agent-platform",
            ValidateAudience = true,
            ValidAudience = securitySection["JwtAudience"] ?? "agent-platform-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        AgentPlatform.Infrastructure.Auth.ApiKeyAuthenticationHandler>(
        "ApiKey", null);

builder.Services.AddAuthorization(options =>
{
    // When authentication is not enforced (QuickStart/dev), allow anonymous by default
    if (!enforceAuth)
    {
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    }
});

builder.Services.Configure<TenantSettings>(builder.Configuration.GetSection("Tenant"));
builder.Services.Configure<ModelDefaults>(builder.Configuration.GetSection("ModelDefaults"));
builder.Services.Configure<RouterSettings>(builder.Configuration.GetSection("Router"));
builder.Services.Configure<PricingSettings>(builder.Configuration.GetSection("Pricing"));
builder.Services.Configure<SecuritySettings>(builder.Configuration.GetSection("Security"));

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (origins is { Length: > 0 })
        options.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
    else
        options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.PostConfigure<RouterSettings>(settings =>
{
    foreach (var c in settings.Candidates ?? [])
    {
        if (string.IsNullOrWhiteSpace(c.ModelId))
            throw new InvalidOperationException("Router candidate ModelId is required");
        if (string.IsNullOrWhiteSpace(c.Provider))
            throw new InvalidOperationException("Router candidate Provider is required");
    }
});

builder.Services.PostConfigure<PricingSettings>(pricing =>
{
    if (pricing.CostPerMillionTokens.Count == 0)
    {
        pricing.CostPerMillionTokens["openai"] = 2.50m;
        pricing.CostPerMillionTokens["anthropic"] = 3.00m;
        pricing.CostPerMillionTokens["deepseek"] = 0.14m;
        pricing.CostPerMillionTokens["qwen"] = 0.40m;
        pricing.CostPerMillionTokens["vllm"] = 0m;
    }
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("PerTenant", context =>
    {
        var tenantId = context.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(
            tenantId, _ => new System.Threading.RateLimiting.TokenBucketRateLimiterOptions
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
        return System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(
            apiKey, _ => new System.Threading.RateLimiting.TokenBucketRateLimiterOptions
            {
                TokenLimit = 50,
                TokensPerPeriod = 50,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
    options.RejectionStatusCode = 429;
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// OpenTelemetry — metrics + tracing
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(DiagnosticsConfig.ServiceName)
        .AddMeter(AgentPlatform.Application.Diagnostics.WorkflowMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

// 初始化数据库（仅开发环境）
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QuickStart"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

    logger.LogInformation("Initializing database...");
    await initializer.InitializeAsync();
    logger.LogInformation("Database initialization completed.");
}

app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<MetricsMiddleware>();
app.UseMiddleware<PromptInjectionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QuickStart"))
{
    // HttpsRedirection disabled in dev profiles without HTTPS endpoint
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();

/// <summary>Entry point class for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
