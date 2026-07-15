using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using MediatR;

namespace AgentPlatform.Application.Agents.Commands.CreateAgent;

internal sealed class CreateAgentCommandHandler : IRequestHandler<CreateAgentCommand, Agent>
{
    private readonly IAgentRepository _repository;

    public CreateAgentCommandHandler(IAgentRepository repository)
    {
        _repository = repository;
    }

    public Task<Agent> Handle(CreateAgentCommand request, CancellationToken ct)
    {
        var endpoint = new ModelEndpoint(
            request.ModelProvider,
            request.ModelName,
            request.ModelApiUrl);

        var role = AgentType.FromCode(request.RoleCode)
            ?? new AgentType(request.RoleCode, request.RoleCode, request.RoleCode);

        var agent = new Agent(
            Guid.NewGuid(),
            request.Name,
            role,
            endpoint,
            request.SystemPrompt,
            request.TenantId);

        _repository.Add(agent);

        return Task.FromResult(agent);
    }
}
