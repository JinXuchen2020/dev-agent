using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AgentPlatform.Infrastructure.Security;

namespace AgentPlatform.Api.Endpoints;

/// <summary>
/// Dev-only simulated-login endpoint for minting JWT tokens during local development.
/// Gated behind Security:DevLoginEnabled (false by default). NEVER enable in production.
/// Token minting is delegated to <see cref="IJwtTokenService"/> (shared with real login).
/// </summary>
internal static class DevLoginEndpoint
{
    public static RouteHandlerBuilder Map(WebApplication app, IConfiguration configuration)
    {
        var tenantSection = configuration.GetSection("Tenant");
        var defaultTenantId = tenantSection.GetValue<Guid?>("DefaultTenantId")
            ?? Guid.Parse("00000000-0000-0000-0000-000000000001");

        return app.MapPost("/api/dev/login", (DevLoginRequest request, IJwtTokenService tokenService) =>
            {
                var tenantId = string.IsNullOrWhiteSpace(request.TenantId)
                    ? defaultTenantId.ToString()
                    : request.TenantId!;
                var role = string.IsNullOrWhiteSpace(request.Role) ? "Admin" : request.Role!;
                var userId = string.IsNullOrWhiteSpace(request.UserId) ? "dev-user" : request.UserId!;
                var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId)
                    ? Guid.Empty.ToString()
                    : request.WorkspaceId!;

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, userId),
                    new(ClaimTypes.Name, userId),
                    new("sub", userId),
                    new("tenant_id", tenantId),
                    new("workspace_id", workspaceId),
                    new(ClaimTypes.Role, role),
                };

                var tokenString = tokenService.CreateToken(claims);

                // Return the raw JWT (no "Bearer " prefix). In Swagger UI / Scalar, open Authorize
                // and paste this token into the bearer field — the UI prepends "Bearer " automatically.
                return Results.Ok(new DevLoginResponse(Token: tokenString, ExpiresAt: DateTime.UtcNow.AddHours(1)));
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
internal sealed record DevLoginRequest(string? TenantId = null, string? Role = null, string? UserId = null, string? WorkspaceId = null);

/// <summary>Response from the dev-only simulated-login endpoint.</summary>
internal sealed record DevLoginResponse(string Token, DateTime ExpiresAt);
