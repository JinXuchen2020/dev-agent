using AgentPlatform.Domain.Aggregates.Agents;

namespace AgentPlatform.Api.Models;

/// <summary>
/// Represents the API response payload returned when querying or creating an agent.
/// </summary>
/// <param name="Id">The unique identifier of the agent.</param>
/// <param name="Name">The display name of the agent.</param>
/// <param name="RoleCode">The role code assigned to the agent within the tenant.</param>
/// <param name="ModelProvider">The name of the model provider backing the agent, if configured.</param>
/// <param name="ModelName">The specific model name used by the agent, if configured.</param>
/// <param name="TenantId">The identifier of the tenant that owns the agent.</param>
/// <param name="CreatedAt">The UTC timestamp when the agent was created.</param>
public record AgentResponse(
    Guid Id,
    string Name,
    string RoleCode,
    string? ModelProvider,
    string? ModelName,
    Guid TenantId,
    DateTime CreatedAt)
{
    /// <summary>
    /// Maps a domain <see cref="Agent"/> aggregate to an <see cref="AgentResponse"/> API payload.
    /// </summary>
    /// <param name="agent">The domain agent aggregate to map from.</param>
    /// <returns>An <see cref="AgentResponse"/> containing the agent's public data.</returns>
    public static AgentResponse From(Agent agent) => new(
        agent.Id,
        agent.Name,
        agent.Role.RoleCode,
        agent.ModelEndpoint.Provider,
        agent.ModelEndpoint.ModelName,
        agent.TenantId,
        agent.CreatedAt);
}
