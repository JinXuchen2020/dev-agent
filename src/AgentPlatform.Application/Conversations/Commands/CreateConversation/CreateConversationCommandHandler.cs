using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.CreateConversation;

internal sealed class CreateConversationCommandHandler : IRequestHandler<CreateConversationCommand, Guid>
{
    private readonly IConversationRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;

    public CreateConversationCommandHandler(
        IConversationRepository repository,
        IAuditLogRepository auditLogRepository)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
    }

    public Task<Guid> Handle(CreateConversationCommand request, CancellationToken ct)
    {
        var conversation = new Conversation(Guid.NewGuid(), request.TenantId);
        _repository.Add(conversation);

        var auditLog = AuditLog.Record(
            tenantId: request.TenantId,
            action: AuditActionType.CreateConversation,
            entity: "Conversation",
            entityId: conversation.Id,
            details: $"Created conversation {conversation.Id}");
        _auditLogRepository.Add(auditLog);

        return Task.FromResult(conversation.Id);
    }
}
