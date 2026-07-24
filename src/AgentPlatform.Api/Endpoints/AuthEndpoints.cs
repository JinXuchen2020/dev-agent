using System.Security.Claims;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Security;

namespace AgentPlatform.Api.Endpoints;

/// <summary>
/// Real authentication endpoints: email + password login (sets an httpOnly cookie)
/// and a current-user identity probe used by the SPA instead of client-side JWT decoding.
/// </summary>
internal static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", async (
            LoginRequest request,
            IUserRepository users,
            IPasswordHasher hasher,
            IJwtTokenService tokenService,
            ITenantProvider tenantProvider,
            HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { title = "Email and password are required." });

            var tenantId = tenantProvider.GetTenantId();
            var user = await users.GetByEmailAsync(tenantId, request.Email.Trim(), http.RequestAborted);
            if (user is null || !user.IsActive || !hasher.Verify(request.Password, user.PasswordHash))
                return Results.Unauthorized();

            var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.Email),
                    new(ClaimTypes.Email, user.Email),
                    new("sub", user.Email),
                    new("tenant_id", user.TenantId.ToString()),
                    new(ClaimTypes.Role, user.Role),
                };
            var token = tokenService.CreateToken(claims);

            // httpOnly + SameSite=Lax cookie; Secure is auto-enabled over HTTPS.
            http.Response.Cookies.Append("ap_access_token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                Path = "/",
                MaxAge = TimeSpan.FromHours(1),
            });

            return Results.Ok(new LoginResponse(new AuthUserDto(
                user.Id.ToString(), user.Email, user.Role, user.TenantId.ToString())));
        })
        .WithTags("Auth")
        .WithSummary("Authenticate with email + password (sets httpOnly cookie)")
        .AllowAnonymous();

        app.MapGet("/api/v1/auth/me", (HttpContext http) =>
        {
            var principal = http.User;
            if (principal.Identity is not { IsAuthenticated: true })
                return Results.Unauthorized();

            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.Name);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            var tenantId = principal.FindFirstValue("tenant_id");

            if (id is null || email is null || role is null || tenantId is null)
                return Results.Unauthorized();

            return Results.Ok(new AuthUserDto(id, email, role, tenantId));
        })
        .WithTags("Auth")
        .WithSummary("Get the current authenticated user's identity");

        // Clears the httpOnly auth cookie (client-side cannot delete it).
        app.MapPost("/api/v1/auth/logout", (HttpContext http) =>
        {
            http.Response.Cookies.Delete("ap_access_token", new CookieOptions { Path = "/" });
            return Results.Ok();
        })
        .WithTags("Auth")
        .WithSummary("Log out by clearing the auth cookie")
        .AllowAnonymous();
    }
}

/// <summary>Request body for <c>POST /api/v1/auth/login</c>.</summary>
internal sealed record LoginRequest(string Email, string Password);

/// <summary>Identity of the authenticated user, returned by login and /auth/me.</summary>
internal sealed record AuthUserDto(string Id, string Email, string Role, string TenantId);

/// <summary>Response from <c>POST /api/v1/auth/login</c>.</summary>
internal sealed record LoginResponse(AuthUserDto User);
