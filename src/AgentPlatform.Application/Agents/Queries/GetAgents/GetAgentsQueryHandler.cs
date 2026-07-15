using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Agents.Queries.GetAgents;

internal sealed class GetAgentsQueryHandler : IRequestHandler<GetAgentsQuery, IEnumerable<Agent>>
{
    private readonly IAgentRepository _repository;
    private readonly ITenantProvider _tenantProvider;

    public GetAgentsQueryHandler(
        IAgentRepository repository,
        ITenantProvider tenantProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
    }

    public async Task<IEnumerable<Agent>> Handle(GetAgentsQuery request, CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetTenantId();
        return await _repository.GetByTenantAsync(tenantId, ct);
    }
}
