using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Agents.Commands.RunAgent;

/// <summary>
/// Runs an autonomous agentic control loop for the supplied agent against a goal and returns
/// the structured result (final answer + full tool-call trace). Used by the
/// <c>POST /api/v1/agents/{id}/runs</c> endpoint and the <see cref="StepType.Agentic"/> workflow node.
/// </summary>
/// <param name="AgentId">The identifier of the agent to drive.</param>
/// <param name="Goal">The objective the agent should accomplish.</param>
/// <param name="RunId">A caller-supplied identifier used to scope and persist run artifacts.</param>
public record RunAgentGoalCommand(Guid AgentId, string Goal, Guid RunId = default) : ICommand<AgenticRunResult>;
