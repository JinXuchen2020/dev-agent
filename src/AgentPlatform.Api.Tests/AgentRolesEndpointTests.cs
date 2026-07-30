using System.Net;
using System.Text;
using System.Text.Json;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// Integration tests for the AgentRoles write endpoints introduced/changed in F19:
/// PUT  /api/v1/agent-roles/{roleCode}  (edit metadata, Admin only)
/// DELETE /api/v1/agent-roles/{roleCode} (delete, Admin only, 409 for built-in / in-use)
///
/// Runs against the full ASP.NET Core pipeline with an in-memory SQLite database.
/// A built-in role is seeded in the shared database so the 409 (BuiltInConflict) path
/// can be exercised against a real row.
/// </summary>
public sealed class AgentRolesEndpointTests : IClassFixture<ApiContractTestFactory>, IAsyncLifetime
{
    private readonly ApiContractTestFactory _factory;

    /// <summary>
    /// Seed role code used to verify the built-in 409 Conflict path.
    /// </summary>
    private const string BuiltInSeedCode = "builtin-seed-f19";

    public AgentRolesEndpointTests(ApiContractTestFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Seed a built-in role so the DELETE 409 path has a real row to reject.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Set<AgentRoleDefinition>().AllAsync(r => r.RoleCode != BuiltInSeedCode))
        {
            db.Set<AgentRoleDefinition>().Add(new AgentRoleDefinition(
                Guid.NewGuid(),
                "Built-in Seed (F19 test)",
                BuiltInSeedCode,
                "Seeded built-in role for contract tests.",
                "You are a seeded built-in role.",
                isBuiltIn: true));
            await db.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Helper that posts a custom role using an Admin client (the create endpoint
    /// is itself Admin-gated) and returns the role code.
    /// </summary>
    private async Task<string> CreateCustomRoleAsync(string roleCode)
    {
        var client = _factory.CreateAuthenticatedClient("Admin");
        var createBody = JsonSerializer.Serialize(new
        {
            name = "Custom QA Role",
            roleCode,
            description = "Created by contract test.",
            systemPrompt = "You are a custom QA agent.",
        });
        var createResponse = await client.PostAsync(
            "/api/v1/agent-roles",
            new StringContent(createBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        return roleCode;
    }

    /// <summary>
    /// Verifies that PUT /api/v1/agent-roles/{roleCode} as Admin updates the role
    /// metadata and echoes the updated values plus isBuiltIn=false.
    /// </summary>
    [Fact]
    public async Task UpdateAgentRole_AsAdmin_Returns200AndUpdatedValues()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");
        var roleCode = "update-target-" + Guid.NewGuid().ToString("N")[..8];
        await CreateCustomRoleAsync(roleCode);

        var updateBody = JsonSerializer.Serialize(new
        {
            name = "Custom QA Role (edited)",
            description = "Edited description.",
            systemPrompt = "You are an edited custom QA agent.",
        });

        // Act
        var response = await client.PutAsync(
            $"/api/v1/agent-roles/{roleCode}",
            new StringContent(updateBody, Encoding.UTF8, "application/json"));

        // Assert — status + content type
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        // Assert — updated values echoed back, and isBuiltIn stays false for custom roles
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(roleCode, root.GetProperty("roleCode").GetString());
        Assert.Equal("Custom QA Role (edited)", root.GetProperty("name").GetString());
        Assert.Equal("Edited description.", root.GetProperty("description").GetString());
        Assert.Equal("You are an edited custom QA agent.", root.GetProperty("systemPrompt").GetString());
        Assert.False(root.GetProperty("isBuiltIn").GetBoolean());

        // Assert — persisted: a subsequent GET returns the edited values
        var getResponse = await client.GetAsync($"/api/v1/agent-roles/{roleCode}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getBody);
        Assert.Equal("Custom QA Role (edited)", getDoc.RootElement.GetProperty("name").GetString());
    }

    /// <summary>
    /// Verifies that PUT with a non-Admin role claim is rejected with 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task UpdateAgentRole_AsNonAdmin_Returns403()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Operator");
        var roleCode = "update-target-" + Guid.NewGuid().ToString("N")[..8];
        await CreateCustomRoleAsync(roleCode);

        var updateBody = JsonSerializer.Serialize(new
        {
            name = "Hijacked Name",
            description = "x",
            systemPrompt = "y",
        });

        // Act
        var response = await client.PutAsync(
            $"/api/v1/agent-roles/{roleCode}",
            new StringContent(updateBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Verifies that PUT without any auth token is rejected with 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task UpdateAgentRole_WithoutAuth_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        var updateBody = JsonSerializer.Serialize(new
        {
            name = "No Auth",
            description = "x",
            systemPrompt = "y",
        });

        // Act
        var response = await client.PutAsync(
            "/api/v1/agent-roles/whatever",
            new StringContent(updateBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Verifies that PUT against a non-existent role code returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task UpdateAgentRole_NotFound_Returns404()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");
        var updateBody = JsonSerializer.Serialize(new
        {
            name = "Phantom",
            description = "x",
            systemPrompt = "y",
        });

        // Act
        var response = await client.PutAsync(
            "/api/v1/agent-roles/does-not-exist-f19",
            new StringContent(updateBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that DELETE against a non-existent role code returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task DeleteAgentRole_NotFound_Returns404()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");

        // Act
        var response = await client.DeleteAsync("/api/v1/agent-roles/does-not-exist-f19");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies that DELETE against a built-in role returns 409 Conflict with a
    /// Problem Details body (Title = "内置角色不可删除").
    /// </summary>
    [Fact]
    public async Task DeleteAgentRole_BuiltIn_Returns409()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");

        // Act
        var response = await client.DeleteAsync($"/api/v1/agent-roles/{BuiltInSeedCode}");

        // Assert — status + problem details content type
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Assert — the problem details title identifies the built-in rejection
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("title", out var title));
        Assert.Equal("内置角色不可删除", title.GetString());

        // Assert — the built-in row was NOT deleted (still resolvable)
        var getResponse = await client.GetAsync($"/api/v1/agent-roles/{BuiltInSeedCode}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    /// <summary>
    /// Verifies that DELETE against a custom (non-built-in, unreferenced) role
    /// succeeds with 204 No Content and the role is gone afterwards (404).
    /// </summary>
    [Fact]
    public async Task DeleteAgentRole_CustomUnused_Returns204AndGone()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient("Admin");
        var roleCode = "delete-target-" + Guid.NewGuid().ToString("N")[..8];
        await CreateCustomRoleAsync(roleCode);

        // Act
        var response = await client.DeleteAsync($"/api/v1/agent-roles/{roleCode}");

        // Assert — deleted
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Assert — subsequent GET confirms removal
        var getResponse = await client.GetAsync($"/api/v1/agent-roles/{roleCode}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
