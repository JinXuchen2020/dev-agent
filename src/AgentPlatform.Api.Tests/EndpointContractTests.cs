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
        var requestUri = "/api/v1/agent-roles";

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
        var requestUri = "/api/v1/execution-logs";

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
        var requestUri = "/api/v1/agent-configurations";

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
    /// Verifies that GET /api/v1/analytics/summary returns 200 OK, a JSON
    /// object, and the consolidated dashboard DTO shape (F18).
    /// </summary>
    [Fact]
    public async Task GetAnalyticsSummary_Returns200AndDashboardShape()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/analytics/summary?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert — status + content type
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", body.Trim());

        // Assert — consolidated dashboard DTO shape (mirrors DashboardSummaryDto)
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("from", out _));
        Assert.True(root.TryGetProperty("to", out _));
        Assert.True(root.TryGetProperty("kpis", out var kpis) && kpis.ValueKind == JsonValueKind.Object);
        Assert.True(kpis.TryGetProperty("activeAgents", out _) && kpis.TryGetProperty("successRate", out _));
        Assert.True(root.TryGetProperty("executionsByDay", out var exec) && exec.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("tokenByDay", out var tok) && tok.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("conversationsByDay", out var conv) && conv.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("latencyByDay", out var lat) && lat.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("topWorkflows", out var top) && top.ValueKind == JsonValueKind.Array);
    }

    /// <summary>
    /// Verifies that GET /api/v1/analytics/summary with an inverted date range
    /// (from &gt; to) returns 400 Bad Request (F18 input boundary).
    /// </summary>
    [Fact]
    public async Task GetAnalyticsSummary_InvertedRange_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient();
        var requestUri = "/api/v1/analytics/summary?from=2026-12-31T00:00:00Z&to=2026-01-01T00:00:00Z";

        // Act
        var response = await client.GetAsync(requestUri);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    // ── F21 工作流触发器端点（匿名 Webhook + 鉴权触发器查询）─────────────

    /// <summary>
    /// 匿名（无 JWT）POST 未知 webhook token 必须返回 404，且不泄露存在性。
    /// 该端点不受 cookie/JWT 约束，仅受 WebhookAnonymous 限流保护。
    /// </summary>
    [Fact]
    public async Task PostWebhook_UnknownToken_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            "/api/v1/webhooks/workflow/does-not-exist-token",
            new StringContent("{\"hello\":\"world\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// 已鉴权 GET /api/v1/workflows/{id}/triggers 必须返回 200 与合法的触发器配置骨架
    /// （webhook/schedule 均为 null，chatBindingCount=0）。不要求工作流存在。
    /// </summary>
    [Fact]
    public async Task GetTriggers_Returns200WithShape()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}/triggers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        // webhook / schedule 为 null 时按 WhenWritingNull 被省略，仅校验 chatBindingCount。
        Assert.True(doc.RootElement.TryGetProperty("chatBindingCount", out var count));
        Assert.Equal(0, count.GetInt32());
    }

    /// <summary>
    /// 未鉴权访问受保护的触发器管理端点必须返回 401。
    /// </summary>
    [Fact]
    public async Task GenerateWebhookToken_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/workflows/{Guid.NewGuid()}/triggers/webhook", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
