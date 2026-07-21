using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AgentPlatform.Api.Diagnostics;
using Microsoft.IdentityModel.Tokens;
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

builder.Services.AddOpenApi(options =>
{
    // Expose a Bearer (JWT) security scheme so the Scalar UI shows an Authorize button.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT bearer token. Use POST /api/dev/login (when Security:DevLoginEnabled=true) to mint a dev token for testing."
        };
        document.SecurityRequirements.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = new System.Collections.Generic.List<string>()
        });
        return System.Threading.Tasks.Task.CompletedTask;
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AgentPlatform API",
        Version = "v1",
        Description = "Multi-agent development platform API for managing agents, conversations, workflows, and model routing."
    });

    // Bearer (JWT) security definition so Swagger UI shows the "Authorize" button.
    // Use POST /api/dev/login (when Security:DevLoginEnabled=true) to mint a dev token, then paste it here.
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer <your JWT token>'. Use POST /api/dev/login (when Security:DevLoginEnabled=true) to mint a dev token for testing."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new System.Collections.Generic.List<string>()
        }
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
var apiKeyHeaderName = securitySection["ApiKeyHeaderName"] ?? "X-API-Key";
// Dev-only "simulated login" endpoint. MUST stay false in production — it mints a
// valid JWT for any tenant/role without credentials.
var devLoginEnabled = securitySection.GetValue<bool>("DevLoginEnabled");

// A single policy scheme ("Smart") is the default for authenticate / challenge / forbid.
// It inspects each request and forwards to JWT ("Bearer") or API-Key ("ApiKey") depending on
// which credential header is present. Without a default scheme, [Authorize] endpoints throw
// "No authenticationScheme was specified, and there was no DefaultChallengeScheme found" when
// authentication is enforced and a request arrives without credentials.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
        options.DefaultAuthenticateScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
        options.DefaultForbidScheme = "Smart";
    })
    .AddPolicyScheme("Smart", "Smart", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (context.Request.Headers.ContainsKey("Authorization"))
                return "Bearer";
            if (!string.IsNullOrEmpty(context.Request.Headers[apiKeyHeaderName].FirstOrDefault()))
                return "ApiKey";
            // No credential present: challenge as Bearer so clients get a WWW-Authenticate hint.
            return "Bearer";
        };
    })
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

// Dev-only "simulated login" so Swagger UI / Scalar can authenticate during local testing.
// Gated behind Security:DevLoginEnabled (false by default). NEVER enable in production.
if (devLoginEnabled)
{
    var tenantSection = builder.Configuration.GetSection("Tenant");
    var defaultTenantId = tenantSection.GetValue<Guid?>("DefaultTenantId")
        ?? Guid.Parse("00000000-0000-0000-0000-000000000001");

    app.MapPost("/api/dev/login", (DevLoginRequest request) =>
    {
        var jwtKey = securitySection["JwtSecretKey"] ?? "dev-secret-key-min-32-chars-long!!";
        var issuer = securitySection["JwtIssuer"] ?? "agent-platform";
        var audience = securitySection["JwtAudience"] ?? "agent-platform-api";

        var tenantId = string.IsNullOrWhiteSpace(request.TenantId)
            ? defaultTenantId.ToString()
            : request.TenantId!;
        var role = string.IsNullOrWhiteSpace(request.Role) ? "Admin" : request.Role!;
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? "dev-user" : request.UserId!;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new("sub", userId),
            new("tenant_id", tenantId),
            new(ClaimTypes.Role, role),
        };

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(1);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        // Return the raw JWT (no "Bearer " prefix). In Swagger UI / Scalar, open Authorize
        // and paste this token into the bearer field — the UI prepends "Bearer " automatically.
        return Results.Ok(new DevLoginResponse(Token: tokenString, ExpiresAt: expires));
    })
    .WithTags("Dev")
    .WithSummary("Mint a dev JWT (simulated login)")
    .AllowAnonymous();
}

app.Run();

/// <summary>Entry point class for WebApplicationFactory in integration tests.</summary>
public partial class Program { }

/// <summary>
/// Request body for the dev-only simulated-login endpoint (<c>POST /api/dev/login</c>).
/// All fields are optional; sensible dev defaults are applied server-side.
/// </summary>
internal sealed record DevLoginRequest(string? TenantId = null, string? Role = null, string? UserId = null);

/// <summary>Response from the dev-only simulated-login endpoint.</summary>
internal sealed record DevLoginResponse(string Token, DateTime ExpiresAt);
