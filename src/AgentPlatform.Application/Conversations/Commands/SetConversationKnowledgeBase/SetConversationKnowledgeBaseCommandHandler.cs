using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Conversations.Commands.SetConversationKnowledgeBase;

internal sealed class SetConversationKnowledgeBaseCommandHandler
    : IRequestHandler<SetConversationKnowledgeBaseCommand, Guid>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository;
    private readonly IAuditLogRepository _auditLogRepository;

    public SetConversationKnowledgeBaseCommandHandler(
        IConversationRepository conversationRepository,
        IKnowledgeBaseRepository knowledgeBaseRepository,
        IAuditLogRepository auditLogRepository)
    {
        _conversationRepository = conversationRepository;
        _knowledgeBaseRepository = knowledgeBaseRepository;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<Guid> Handle(SetConversationKnowledgeBaseCommand request, CancellationToken ct)
    {
        var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, ct);
        if (conversation == null)
            throw new ArgumentException($"Conversation '{request.ConversationId}' not found");

        var kb = await _knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, ct);
        if (kb == null)
            throw new ArgumentException($"Knowledge base '{request.KnowledgeBaseId}' not found");
        if (kb.TenantId != request.TenantId)
            throw new InvalidOperationException(
                $"Knowledge base '{request.KnowledgeBaseId}' does not belong to the current tenant");

        conversation.AttachKnowledgeBase(kb.Id, kb.CollectionName);

        var auditLog = AuditLog.Record(
            tenantId: request.TenantId,
            action: AuditActionType.UpdateConversation,
            entity: "Conversation",
            entityId: request.ConversationId,
            details: $"Linked conversation {request.ConversationId} to knowledge base {kb.Id} (collection={kb.CollectionName})");
        _auditLogRepository.Add(auditLog);

        return conversation.Id;
    }
}
