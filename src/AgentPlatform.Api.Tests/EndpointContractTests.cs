using System.Net;
using System.Text.Json;
using Xunit;

namespace AgentPlatform.Api.Tests;

/// <summary>
/// API contract tests that verify each controller endpoint returns the
/// correct HTTP status code, response content type, and JSON structure.
///
/// Uses the full ASP.NET Core pipeline (real controllers, real middleware)
/// against an in-memory SQLite database with a valid JWT token for
/// authenticated requests. Routing is case-insensitive so paths are
/// written in lowercase for readability.
/// </summary>
public sealed class EndpointContractTests : IClassFixture<ApiContractTestFactory>
{
    private readonly ApiContractTestFactory _factory;

    /// <summary>
    /// Initializes a new test instance with the shared test factory.
    /// </summary>
    /// <param name="factory">The custom WebApplicationFactory providing the test server.</param>
    public EndpointContractTests(ApiContractTestFactory factory)
    {
        _factory = factory;
    }

    // ── Collection endpoints that return JSON arrays ─────────────────

    /// <summary>
    /// Verifies that GET /api/v1/agents returns 200 OK and a JSON array.
    /// </summary>
    [Fact]
    public async Task GetAgents_Returns200AndJsonArray()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/Agents";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("[", body.Trim());
    }

    /// <summary>
    /// Verifies that GET /api/v1/AgentRoles returns 200 OK and a JSON array.
    /// </summary>
    [Fact]
    public async Task GetAgentRoles_Returns200AndJsonArray()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/AgentRoles";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("[", body.Trim());
    }

    /// <summary>
    /// Verifies that GET /api/v1/Conversations returns 200 OK and a JSON array.
    /// </summary>
    [Fact]
    public async Task GetConversations_Returns200AndJsonArray()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/Conversations";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("[", body.Trim());
    }

    // ── Paginated endpoints that return JSON objects ──────────────────

    /// <summary>
    /// Verifies that GET /api/v1/ExecutionLogs returns 200 OK and a JSON
    /// object with an "items" property (paginated response envelope).
    /// </summary>
    [Fact]
    public async Task GetExecutionLogs_Returns200AndJsonObject()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/ExecutionLogs";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", body.Trim());

        // Verify the paginated envelope structure
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out var totalCount));
        Assert.Equal(JsonValueKind.Number, totalCount.ValueKind);
    }

    /// <summary>
    /// Verifies that GET /api/v1/workflows returns 200 OK and a JSON object
    /// with an "items" property (paginated response envelope).
    /// </summary>
    [Fact]
    public async Task GetWorkflows_Returns200AndJsonObject()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/workflows";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", body.Trim());

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out var totalCount));
        Assert.Equal(JsonValueKind.Number, totalCount.ValueKind);
    }

    /// <summary>
    /// Verifies that GET /api/v1/AgentConfigurations returns 200 OK and a
    /// JSON object with an "items" property (paginated response envelope).
    /// </summary>
    [Fact]
    public async Task GetAgentConfigurations_Returns200AndJsonObject()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/AgentConfigurations";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", body.Trim());

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out var totalCount));
        Assert.Equal(JsonValueKind.Number, totalCount.ValueKind);
    }

    // ── Infrastructure endpoints ──────────────────────────────────────

    /// <summary>
    /// Verifies that GET /health returns 200 OK.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_Returns200()
    {
        // Arrange — health check is mapped at /health, not under api/v1
        var client = _factory.CreateClient();
        var requestUri = "/health";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Verifies that GET /metrics returns 200 OK with Prometheus
    /// text-format content.
    /// </summary>
    [Fact]
    public async Task MetricsEndpoint_Returns200()
    {
        // Arrange — Prometheus is mapped at /metrics, not under api/v1
        var client = _factory.CreateClient();
        var requestUri = "/metrics";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Prometheus metrics are served as text/plain (not JSON)
        var contentType = response.Content.Headers.ContentType;
        Assert.NotNull(contentType);
        Assert.Contains("text/plain", contentType.MediaType, StringComparison.OrdinalIgnoreCase);
    }

    // ── Error contract ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a request to a non-existent API endpoint returns 404
    /// and a <c>application/problem+json</c> response body conforming to
    /// RFC 9457 (Problem Details).
    /// </summary>
    [Fact]
    public async Task NonExistentEndpoint_ReturnsProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();
        var requestUri = "/api/v1/non-existent-endpoint";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert — status code
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Assert — content type is Problem Details JSON
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Assert — body contains standard Problem Details fields
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("type", out var typeProperty));
        Assert.Equal(JsonValueKind.String, typeProperty.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(typeProperty.GetString()));

        Assert.True(doc.RootElement.TryGetProperty("title", out var titleProperty));
        Assert.Equal(JsonValueKind.String, titleProperty.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(titleProperty.GetString()));

        Assert.True(doc.RootElement.TryGetProperty("status", out var statusProperty));
        Assert.Equal(JsonValueKind.Number, statusProperty.ValueKind);
        Assert.Equal(404, statusProperty.GetInt32());
    }
}
