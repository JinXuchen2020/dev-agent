using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;

namespace AgentPlatform.Application.Routing.Services;

/// <summary>
/// Represents a request to route a conversation through the model router.
/// </summary>
/// <param name="TenantId">The unique identifier of the tenant making the request.</param>
/// <param name="Messages">The read-only list of chat messages to send to the selected model.</param>
/// <param name="PreferredModel">An optional model identifier to prioritize during routing. When specified, the preferred model is attempted first.</param>
/// <param name="Tools">Optional tool definitions the model is allowed to invoke. Forwarded to the underlying model client.</param>
public record RoutingRequest(
    Guid TenantId,
    IReadOnlyList<ChatMessage> Messages,
    string? PreferredModel = null,
    IReadOnlyList<ToolDefinition>? Tools = null);
