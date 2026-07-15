using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Agents.Queries.GetAgent;

internal sealed class GetAgentQueryHandler : IRequestHandler<GetAgentQuery, Agent?>
{
    private readonly IAgentRepository _repository;

    public GetAgentQueryHandler(IAgentRepository repository)
    {
        _repository = repository;
    }

    public async Task<Agent?> Handle(GetAgentQuery request, CancellationToken ct)
    {
        return await _repository.GetByIdAsync(request.AgentId, ct);
    }
}
