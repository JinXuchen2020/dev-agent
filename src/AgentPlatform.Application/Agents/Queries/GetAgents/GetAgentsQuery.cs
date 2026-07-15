using MediatR;
using AgentPlatform.Domain.Aggregates.Agents;

namespace AgentPlatform.Application.Agents.Queries.GetAgents;

/// <summary>
/// Represents a query to retrieve all agents belonging to the current tenant.
/// </summary>
public record GetAgentsQuery : IRequest<IEnumerable<Agent>>;
