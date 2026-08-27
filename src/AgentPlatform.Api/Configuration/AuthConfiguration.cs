using System.Text;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace AgentPlatform.Api.Configuration;

/// <summary>
/// Configures JWT/API-Key authentication via a "Smart" policy scheme,
/// authorization policies, and strongly-typed options binding.
/// </summary>
internal static class AuthConfiguration
{
    public static IServiceCollection AddAuthConfiguration(
        this IServiceCollection services, IConfiguration configuration)
    {
        var securitySection = configuration.GetSection("Security");
        var enforceAuth = securitySection.GetValue<bool>("EnforceAuthentication");
        var apiKeyHeaderName = securitySection["ApiKeyHeaderName"] ?? "X-API-Key";

        // A single policy scheme ("Smart") is the default for authenticate / challenge / forbid.
        // It inspects each request and forwards to JWT ("Bearer") or API-Key ("ApiKey") depending
        // on which credential header is present. Without a default scheme, [Authorize] endpoints
        // throw when authentication is enforced and a request arrives without credentials.
        services.AddAuthentication(options =>
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
                    // httpOnly cookie auth: present cookie → validate as Bearer.
                    if (context.Request.Cookies.ContainsKey("ap_access_token"))
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
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = securitySection["JwtIssuer"] ?? "agent-platform",
                    ValidateAudience = true,
                    ValidAudience = securitySection["JwtAudience"] ?? "agent-platform-api",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                // Read the JWT from the httpOnly cookie when no Authorization header is present.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var cookie = context.Request.Cookies["ap_access_token"];
                        if (!string.IsNullOrEmpty(cookie))
                            context.Token = cookie;
                        return Task.CompletedTask;
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);

        services.AddAuthorization(options =>
        {
            // When authentication is not enforced (QuickStart/dev), allow anonymous by default
            if (!enforceAuth)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
            }
        });

        // Strongly-typed options configuration
        services.Configure<TenantSettings>(configuration.GetSection("Tenant"));
        services.Configure<RouterSettings>(configuration.GetSection("Router"));
        services.Configure<PricingSettings>(configuration.GetSection("Pricing"));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));

        services.PostConfigure<PricingSettings>(pricing =>
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

        return services;
    }
}
