using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Queries.GetConversationById;

internal sealed class GetConversationByIdQueryHandler
    : IRequestHandler<GetConversationByIdQuery, Conversation?>
{
    private readonly IConversationRepository _conversationRepository;

    public GetConversationByIdQueryHandler(IConversationRepository conversationRepository)
    {
        _conversationRepository = conversationRepository;
    }

    public Task<Conversation?> Handle(GetConversationByIdQuery request, CancellationToken ct)
    {
        return _conversationRepository.GetByIdWithMessagesAsync(request.Id, ct);
    }
}
