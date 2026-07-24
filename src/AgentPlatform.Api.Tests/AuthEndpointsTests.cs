using System.Net;
using System.Text;
using System.Text.Json;
using AgentPlatform.Domain.Aggregates.Users;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Persistence;
using AgentPlatform.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// Integration tests for the real cookie-based auth endpoints introduced in F2:
/// POST /api/v1/auth/login (sets an httpOnly cookie), GET /api/v1/auth/me
/// (reads identity from the cookie), and POST /api/v1/auth/logout.
///
/// Runs against the full ASP.NET Core pipeline with an in-memory SQLite
/// database. The default admin user (admin@acme.io / Admin@123456) is seeded
/// by this fixture on startup (production seeds it via DatabaseInitializer,
/// which only runs in Development/QuickStart — not in the Test environment
/// used here).
///
/// Cookies are managed explicitly (HandleCookies = false) so each test is
/// isolated and the Set-Cookie / Cookie exchange is asserted directly.
/// </summary>
public sealed class AuthEndpointsTests : IClassFixture<ApiContractTestFactory>, IAsyncLifetime
{
    private readonly ApiContractTestFactory _factory;

    /// <summary>
    /// Initializes a new test instance with the shared test factory.
    /// </summary>
    public AuthEndpointsTests(ApiContractTestFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Seed the default admin user so the login endpoint has a known identity.
        // Idempotent: only seeds when the Users table is empty.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.CountAsync() == 0)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var tenantSettings = scope.ServiceProvider.GetRequiredService<IOptions<TenantSettings>>().Value;
            var defaultTenantId = tenantSettings.DefaultTenantId != Guid.Empty
                ? tenantSettings.DefaultTenantId
                : new Guid("00000000-0000-0000-0000-000000000001");
            db.Users.Add(new User(
                Guid.NewGuid(),
                defaultTenantId,
                "admin@acme.io",
                hasher.Hash("Admin@123456"),
                "Admin"));
            await db.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Extracts the <c>ap_access_token</c> value from a response's Set-Cookie header.
    /// </summary>
    private static string? ExtractAuthCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;
        foreach (var header in values)
        {
            // header format: ap_access_token=<jwt>; path=/; httponly; samesite=lax
            var name = header.Split(';', 2)[0];
            var pair = name.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim() == "ap_access_token")
                return pair[1].Trim();
        }
        return null;
    }

    /// <summary>
    /// Verifies that a valid email + password returns 200, sets the
    /// <c>ap_access_token</c> cookie, and that replaying the cookie to
    /// GET /auth/me returns the user's identity.
    /// </summary>
    [Fact]
    public async Task Login_WithValidCredentials_SetsCookieAndMeReturnsUser()
    {
        // Arrange — cookie-less client so we control the cookie exchange.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginBody = JsonSerializer.Serialize(new { email = "admin@acme.io", password = "Admin@123456" });

        // Act — login
        var loginResponse = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(loginBody, Encoding.UTF8, "application/json"));

        // Assert — login succeeded and a cookie was set
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = ExtractAuthCookie(loginResponse);
        Assert.False(string.IsNullOrEmpty(token), "Expected ap_access_token cookie to be set on login.");

        // Act — GET /auth/me replaying the cookie
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Add("Cookie", $"ap_access_token={token}");
        var meResponse = await client.SendAsync(meRequest);

        // Assert — identity is resolved from the cookie
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meBody = await meResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(meBody);
        Assert.True(doc.RootElement.TryGetProperty("email", out var email));
        Assert.Equal("admin@acme.io", email.GetString());
        Assert.True(doc.RootElement.TryGetProperty("role", out var role));
        Assert.Equal("Admin", role.GetString());
    }

    /// <summary>
    /// Verifies that a wrong password returns 401 and does not set a cookie.
    /// </summary>
    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginBody = JsonSerializer.Serialize(new { email = "admin@acme.io", password = "WrongPassword!" });

        // Act
        var response = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(loginBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(string.IsNullOrEmpty(ExtractAuthCookie(response)), "No cookie should be set on failed login.");
    }

    /// <summary>
    /// Verifies that missing email/password returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task Login_WithMissingFields_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginBody = JsonSerializer.Serialize(new { email = "", password = "" });

        // Act
        var response = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(loginBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that GET /auth/me without any auth cookie returns 401.
    /// </summary>
    [Fact]
    public async Task Me_WithoutAuth_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Act
        var response = await client.GetAsync("/api/v1/auth/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Verifies that logout clears the auth cookie so a subsequent
    /// GET /auth/me (without the cookie) is unauthorized.
    /// </summary>
    [Fact]
    public async Task Logout_ClearsCookieAndMeBecomesUnauthorized()
    {
        // Arrange — log in and capture the cookie.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginBody = JsonSerializer.Serialize(new { email = "admin@acme.io", password = "Admin@123456" });
        var loginResponse = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(loginBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = ExtractAuthCookie(loginResponse);
        Assert.False(string.IsNullOrEmpty(token));

        // Act — logout with the cookie
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add("Cookie", $"ap_access_token={token}");
        var logoutResponse = await client.SendAsync(logoutRequest);

        // Assert — logout itself succeeds
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Act — a fresh /auth/me with no cookie
        var meResponse = await client.GetAsync("/api/v1/auth/me");

        // Assert — without the cookie, the user is unauthenticated
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }
}
