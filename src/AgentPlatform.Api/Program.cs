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

// ── Model client startup validation ────────────────────────────────────
// 仅 Test/Integration 环境强制校验真实 Key（测试需跑真实 LLM）。
// Development/Production/Staging 正常启动不强制 Key —— 运行时由 PlatformModelsProvider
// 回退 OpenAI:* 配置，或租户通过「我的凭据」自行配置 BYO Key；无可用模型时路由抛出 ModelNotConfiguredException。
{
    var modelModeLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var openAiKeyConfigured = !string.IsNullOrEmpty(app.Configuration["OpenAI:Key"]);

    if (app.Environment.IsEnvironment("Test"))
    {
        modelModeLogger.LogInformation("模型客户端：测试环境使用 StubModelClient（仅测试隔离，不影响运行环境）。");
    }
    else if (app.Environment.IsEnvironment("Integration"))
    {
        if (openAiKeyConfigured)
        {
            modelModeLogger.LogInformation("模型客户端已接入真实 LLM 端点（集成测试环境，OpenAI 兼容协议）。");
        }
        else
        {
            var msg = "Integration environment requires OpenAI:Key (env OPENAI_API_KEY) for real LLM integration tests.";
            modelModeLogger.LogCritical(msg);
            throw new InvalidOperationException(msg);
        }
    }
    else
    {
        if (openAiKeyConfigured)
        {
            modelModeLogger.LogInformation("模型客户端已接入真实 LLM 端点（平台级配置，OpenAI 兼容协议）。");
        }
        else
        {
            modelModeLogger.LogWarning("未配置平台级 OpenAI:Key —— 启动继续；运行时将回退 OpenAI:* 配置或租户 BYO 凭据。" +
                                       "如无可用模型，首次路由会抛出 ModelNotConfiguredException。" +
                                       "请在「我的凭据」配置 BYO Key 或设置环境变量 OPENAI_API_KEY。");
        }
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
