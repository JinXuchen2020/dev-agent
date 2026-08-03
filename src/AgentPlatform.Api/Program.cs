using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPlatform.Api.Configuration;
using AgentPlatform.Api.Endpoints;
using AgentPlatform.Api.Middleware;
using AgentPlatform.Api.Security;
using AgentPlatform.Application;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure;
using AgentPlatform.Infrastructure.Persistence;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Service registration ──────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new Asp.Versioning.UrlSegmentApiVersionReader();
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.WorkflowConflictExceptionHandler>();
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.PublishedWorkflowExceptionHandler>();
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.WorkflowGraphExceptionHandler>();
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.UnsupportedContentTypeExceptionHandler>();
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.InvalidYamlExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddOpenApiConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);
builder.Services.AddInfrastructureConfiguration(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AgentPlatform.Infrastructure.Security.IJwtTokenService, JwtTokenService>();

// JWT startup guard — reject dev default key outside development
var jwtKey = builder.Configuration["Security:JwtSecretKey"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey == "dev-secret-key-min-32-chars-long!!")
    throw new InvalidOperationException("Security:JwtSecretKey must be configured and must not be the dev default.");

var app = builder.Build();

// ── Database initialization (development only) ────────────────────
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QuickStart"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    logger.LogInformation("Initializing database...");
    await initializer.InitializeAsync();
    logger.LogInformation("Database initialization completed.");
}

// ── Model client mode diagnostic ──────────────────────────────
// 明确当前模型模式，避免「无密钥 + Stub 回退」时用户误以为配置正常却只收到模拟回复。
{
    var modelModeLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var modelProviderCfg = app.Configuration.GetSection("ModelClient:Provider").Value;
    var llmConfiguredNow = !string.IsNullOrEmpty(app.Configuration["OpenAI:Key"])
        || !string.IsNullOrEmpty(app.Configuration["DeepSeek:Key"])
        || !string.IsNullOrEmpty(app.Configuration["VLLM:Url"]);
    if (string.Equals(modelProviderCfg, "Stub", StringComparison.Ordinal) || !llmConfiguredNow)
        modelModeLogger.LogWarning(
            "模型客户端使用 StubModelClient（未配置真实 LLM：OpenAI:Key / DeepSeek:Key / VLLM:Url 全为空）。" +
            "会话消息将返回模拟回复。要接入真实模型请设置上述任一密钥。");
    else
        modelModeLogger.LogInformation("模型客户端已接入真实 LLM 端点（{Provider}）。", modelProviderCfg);
}

// ── OpenAPI / Swagger / Scalar pipeline ───────────────────────────
app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwagger();
app.UseSwaggerUI();

// ── Middleware pipeline ───────────────────────────────────────────
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<MetricsMiddleware>();
app.UseMiddleware<PromptInjectionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("QuickStart"))
    app.UseHttpsRedirection();

app.UseCors();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");

// ── Dev-only endpoints ────────────────────────────────────────────
if (builder.Configuration.GetValue<bool>("Security:DevLoginEnabled"))
    DevLoginEndpoint.Map(app, builder.Configuration);

// ── Auth endpoints (real email+password login + /auth/me) ─────────
AuthEndpoints.Map(app);

app.Run();

/// <summary>Entry point class for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
