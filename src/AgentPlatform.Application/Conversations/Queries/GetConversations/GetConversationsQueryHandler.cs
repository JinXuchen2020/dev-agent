using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.GetConversations;

internal sealed class GetConversationsQueryHandler : IRequestHandler<GetConversationsQuery, IEnumerable<Conversation>>
{
    private readonly IConversationRepository _repository;
    private readonly ITenantProvider _tenantProvider;

    public GetConversationsQueryHandler(
        IConversationRepository repository,
        ITenantProvider tenantProvider)
    {
        _repository = repository;
        _tenantProvider = tenantProvider;
    }

    public async Task<IEnumerable<Conversation>> Handle(GetConversationsQuery request, CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetTenantId();
        return await _repository.GetByTenantAsync(tenantId, ct);
    }
}
