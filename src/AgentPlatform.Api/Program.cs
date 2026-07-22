using System.Text.Json;
using System.Text.Json.Serialization;
using AgentPlatform.Api.Configuration;
using AgentPlatform.Api.Endpoints;
using AgentPlatform.Api.Middleware;
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
builder.Services.AddHealthChecks();

builder.Services.AddOpenApiConfiguration();
builder.Services.AddAuthConfiguration(builder.Configuration);
builder.Services.AddInfrastructureConfiguration(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.Run();

/// <summary>Entry point class for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
