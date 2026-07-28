using AgentPlatform.Application.Abstractions;

namespace AgentPlatform.Application.Agents.Commands.DeleteAgent;

/// <summary>
/// Represents a command to delete an existing agent by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the agent to delete.</param>
public record DeleteAgentCommand(Guid Id) : ICommand<bool>;
