using AgentPlatform.Application.Agents.Agentic;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Agents.Commands.RunAgent;

/// <summary>
/// Handles <see cref="RunAgentGoalCommand"/>: loads the agent and drives the
/// <see cref="AgenticOrchestrator"/> ReAct control loop to completion.
/// </summary>
internal sealed class RunAgentGoalCommandHandler : IRequestHandler<RunAgentGoalCommand, AgenticRunResult>
{
    private readonly IAgentRepository _repository;
    private readonly AgenticOrchestrator _orchestrator;

    public RunAgentGoalCommandHandler(IAgentRepository repository, AgenticOrchestrator orchestrator)
    {
        _repository = repository;
        _orchestrator = orchestrator;
    }

    public async Task<AgenticRunResult> Handle(RunAgentGoalCommand request, CancellationToken ct)
    {
        var agent = await _repository.GetByIdAsync(request.AgentId, ct);
        if (agent is null)
            throw new InvalidOperationException($"Agent '{request.AgentId}' not found.");

        return await _orchestrator.RunGoalAsync(request.Goal, agent, request.RunId, ct);
    }
}
