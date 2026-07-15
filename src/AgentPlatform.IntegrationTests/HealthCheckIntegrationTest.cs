using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentPlatform.IntegrationTests;

/// <summary>
/// Basic integration test verifying the API boots and responds.
/// Uses WebApplicationFactory to spin up the API in-memory.
/// </summary>
public sealed class HealthCheckIntegrationTest
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckIntegrationTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_Should_Return200()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwaggerUi_Should_BeAccessible()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ScalarUi_Should_BeAccessible()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/scalar/v1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
