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

// ── Database initialization (Development / Integration) ─
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Integration"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
    logger.LogInformation("Initializing database...");
    await initializer.InitializeAsync();
    logger.LogInformation("Database initialization completed.");
}

// ── Model client startup validation (fail-fast for non-Test environments) ─────
// Test 环境使用 StubModelClient，其他环境强制要求配置 OpenAI Key（含 DeepSeek/vLLM 均走 OpenAI 兼容协议）。
// 注意：Integration 环境（仅 SpecFlow 测试使用）同样强制要求真实 Key —— 集成测试必须跑真实 LLM。
{
    var modelModeLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var openAiKeyConfigured = !string.IsNullOrEmpty(app.Configuration["OpenAI:Key"]);

    if (app.Environment.IsEnvironment("Test"))
    {
        modelModeLogger.LogInformation("模型客户端：测试环境使用 StubModelClient（仅测试隔离，不影响运行环境）。");
    }
    else if (openAiKeyConfigured)
    {
        modelModeLogger.LogInformation("模型客户端已接入真实 LLM 端点（平台级配置，OpenAI 兼容协议）。");
    }
    else
    {
        var msg = "No OpenAI API Key configured. Set OpenAI:Key (env OPENAI_API_KEY) " +
                  "for OpenAI/DeepSeek/vLLM (all OpenAI-compatible). " +
                  "Optional: OpenAI:BaseUrl (env OPENAI_BASE_URL) to override endpoint. " +
                  "Test environment is exempt and uses StubModelClient.";
        modelModeLogger.LogCritical(msg);
        throw new InvalidOperationException(msg);
    }
}

// ── OpenAPI / Swagger / Scalar pipeline ───────────────────────────
app.MapOpenApi();
app.MapScalarApiReference();
app.UseSwagger();
app.UseSwaggerUI();

// ── Middleware pipeline ───────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<MetricsMiddleware>();
app.UseMiddleware<PromptInjectionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
// F35（决策 D3=B）：剥离非可见工作空间的 X-Workspace-Id 头，防止伪造头绕过成员可见性。
app.UseMiddleware<WorkspaceHeaderGuardMiddleware>();
if (app.Configuration.GetValue<bool>("Security:RateLimitingEnabled", true))
    app.UseRateLimiter();

if (!app.Environment.IsDevelopment())
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
