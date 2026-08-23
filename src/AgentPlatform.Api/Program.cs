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
builder.Services.AddExceptionHandler<AgentPlatform.Api.Exceptions.KeyNotFoundExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddOpenApiConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);
builder.Services.AddInfrastructureConfiguration(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<AgentPlatform.Infrastructure.Security.IJwtTokenService, JwtTokenService>();

// JWT startup guard — reject dev default key outside development
var jwtKey = builder.Configuration["Security:JwtSecretKey"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey == "dev-secret-key-min-32-chars-long!!")
    throw new InvalidOperationException("Security:JwtSecretKey must be configured and must not be the dev default.");

var app = builder.Build();

// ── Database initialization (development / QuickStart / Integration) ─
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QuickStart")
    || app.Environment.IsEnvironment("Integration"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    logger.LogInformation("Initializing database...");
    await initializer.InitializeAsync();
    logger.LogInformation("Database initialization completed.");
}

// ── Model client mode diagnostic ──────────────────────────────
// 运行环境不再静默回退 Stub：未配置任何模型 provider 时，调用将直接报错。
{
    var modelModeLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var llmConfiguredNow = !string.IsNullOrEmpty(app.Configuration["OpenAI:Key"])
        || !string.IsNullOrEmpty(app.Configuration["DeepSeek:Key"])
        || !string.IsNullOrEmpty(app.Configuration["VLLM:Url"]);

    if (app.Environment.IsEnvironment("Test") || app.Environment.IsEnvironment("Integration"))
        modelModeLogger.LogInformation("模型客户端：测试环境使用 StubModelClient（仅测试隔离，不影响运行环境）。");
    else if (llmConfiguredNow)
        modelModeLogger.LogInformation("模型客户端已接入真实 LLM 端点（平台级配置）。");
    else
        modelModeLogger.LogWarning(
            "未配置任何平台级模型 provider（OpenAI:Key / DeepSeek:Key / VLLM:Url 全为空）。" +
            "调用模型时若当前租户未在「我的凭据」中添加模型，将直接报错。" +
            "请配置真实 LLM 端点，或在「我的凭据」中添加 BYO 模型凭据。");
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
if (app.Configuration.GetValue<bool>("Security:RateLimitingEnabled", true))
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
