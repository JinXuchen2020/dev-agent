using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;

namespace AgentPlatform.Application.Agents.Commands.CreateAgent;

/// <summary>
/// Represents a command to create a new agent with the specified configuration.
/// </summary>
/// <param name="Name">The display name of the agent.</param>
/// <param name="RoleCode">The role code assigned to the agent (e.g., "developer", "architect").</param>
    /// <param name="ModelProvider">The provider hosting the model (e.g., "openai", "anthropic").</param>
/// <param name="ModelName">The name of the model to use (e.g., "gpt-4o").</param>
/// <param name="ModelApiUrl">The API base URL for the model provider.</param>
/// <param name="SystemPrompt">The system prompt that defines the agent's behaviour.</param>
/// <param name="TenantId">The unique identifier of the tenant that owns the agent.</param>
public record CreateAgentCommand(
    string Name,
    string RoleCode,
    string ModelProvider,
    string ModelName,
    string ModelApiUrl,
    string SystemPrompt,
    Guid TenantId
) : ICommand<Agent>;
