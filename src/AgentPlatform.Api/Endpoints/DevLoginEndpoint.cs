using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AgentPlatform.Api.Endpoints;

/// <summary>
/// Dev-only simulated-login endpoint for minting JWT tokens during local development.
/// Gated behind Security:DevLoginEnabled (false by default). NEVER enable in production.
/// </summary>
internal static class DevLoginEndpoint
{
    public static RouteHandlerBuilder Map(WebApplication app, IConfiguration configuration)
    {
        var securitySection = configuration.GetSection("Security");
        var tenantSection = configuration.GetSection("Tenant");
        var defaultTenantId = tenantSection.GetValue<Guid?>("DefaultTenantId")
            ?? Guid.Parse("00000000-0000-0000-0000-000000000001");

        return app.MapPost("/api/dev/login", (DevLoginRequest request) =>
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

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
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
}

/// <summary>
/// Request body for the dev-only simulated-login endpoint (<c>POST /api/dev/login</c>).
/// All fields are optional; sensible dev defaults are applied server-side.
/// </summary>
internal sealed record DevLoginRequest(string? TenantId = null, string? Role = null, string? UserId = null);

/// <summary>Response from the dev-only simulated-login endpoint.</summary>
internal sealed record DevLoginResponse(string Token, DateTime ExpiresAt);
