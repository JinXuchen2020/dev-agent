using System.Linq;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
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
        var conversations = (await _repository.GetByTenantAsync(tenantId, ct)).AsEnumerable();

        if (request.Status.HasValue)
        {
            conversations = conversations.Where(c => c.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var kw = request.Q.Trim();
            conversations = conversations.Where(c =>
                c.Id.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (c.WorkflowId?.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.KnowledgeBaseId?.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.CollectionName?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                c.Messages.Any(m => (m.Content?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        return conversations;
    }
}
