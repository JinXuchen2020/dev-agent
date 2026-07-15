using AgentPlatform.Domain.Aggregates.Agents;
using MediatR;

namespace AgentPlatform.Application.Agents.Queries.GetAgent;

/// <summary>
/// Represents a query to retrieve an agent by its unique identifier.
/// </summary>
/// <param name="AgentId">The unique identifier of the agent to retrieve.</param>
public record GetAgentQuery(Guid AgentId) : IRequest<Agent?>;
