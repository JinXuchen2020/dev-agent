using AgentPlatform.Domain.Aggregates.Agents;

using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Agents.Commands.UpdateAgent;

/// <summary>
/// Represents a command to update an existing agent's mutable properties.
/// Null fields are left unchanged.
/// </summary>
/// <param name="Id">The unique identifier of the agent to update.</param>
/// <param name="Name">The new display name, or null to keep the current value.</param>
/// <param name="RoleCode">The new role code, or null to keep the current value.</param>
/// <param name="ModelProvider">The new model provider, or null to keep the current value.</param>
/// <param name="ModelName">The new model name, or null to keep the current value.</param>
/// <param name="ModelApiUrl">The new model API URL, or null to keep the current value.</param>
/// <param name="SystemPrompt">The new system prompt, or null to keep the current value.</param>
/// <param name="Status">The new status, or null to keep the current value.</param>
public record UpdateAgentCommand(
    Guid Id,
    string? Name,
    string? RoleCode,
    string? ModelProvider,
    string? ModelName,
    string? ModelApiUrl,
    string? SystemPrompt,
    string? Status
) : ICommand<Agent?>;
