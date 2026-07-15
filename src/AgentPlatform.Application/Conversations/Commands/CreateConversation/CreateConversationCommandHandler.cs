using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.CreateConversation;

internal sealed class CreateConversationCommandHandler : IRequestHandler<CreateConversationCommand, Guid>
{
    private readonly IConversationRepository _repository;

    public CreateConversationCommandHandler(IConversationRepository repository)
    {
        _repository = repository;
    }

    public Task<Guid> Handle(CreateConversationCommand request, CancellationToken ct)
    {
        var conversation = new Conversation(Guid.NewGuid(), request.TenantId);
        _repository.Add(conversation);
        return Task.FromResult(conversation.Id);
    }
}
