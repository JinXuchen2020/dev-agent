using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.RemoveConversationKnowledgeBase;

internal sealed class RemoveConversationKnowledgeBaseCommandHandler
    : IRequestHandler<RemoveConversationKnowledgeBaseCommand, Guid>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IAuditLogRepository _auditLogRepository;

    public RemoveConversationKnowledgeBaseCommandHandler(
        IConversationRepository conversationRepository,
        IAuditLogRepository auditLogRepository)
    {
        _conversationRepository = conversationRepository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<Guid> Handle(RemoveConversationKnowledgeBaseCommand request, CancellationToken ct)
    {
        var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, ct);
        if (conversation == null)
            throw new ArgumentException($"Conversation '{request.ConversationId}' not found");

        conversation.DetachKnowledgeBase();

        var auditLog = AuditLog.Record(
            tenantId: request.TenantId,
            action: AuditActionType.UpdateConversation,
            entity: "Conversation",
            entityId: request.ConversationId,
            details: $"Unlinked conversation {request.ConversationId} from its knowledge base");
        _auditLogRepository.Add(auditLog);

        return conversation.Id;
    }
}
