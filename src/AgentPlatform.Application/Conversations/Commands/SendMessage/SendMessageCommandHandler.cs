using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Routing;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Conversations.Commands.SendMessage;

internal sealed class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResult>
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IModelRouter _router;
    private readonly IVectorStore _vectorStore;
    private readonly ModelDefaults _defaults;
    private readonly ITenantProvider _tenant;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly RagSettings _ragSettings;
    private readonly ILogger<SendMessageCommandHandler> _logger;

    public SendMessageCommandHandler(
        IConversationRepository conversationRepository,
        IModelRouter router,
        IVectorStore vectorStore,
        IOptions<ModelDefaults> defaultsOptions,
        ITenantProvider tenant,
        IAuditLogRepository auditLogRepository,
        IOptions<RagSettings> ragOptions,
        ILogger<SendMessageCommandHandler> logger)
    {
        _conversationRepository = conversationRepository;
        _router = router;
        _vectorStore = vectorStore;
        _defaults = defaultsOptions.Value;
        _tenant = tenant;
        _auditLogRepository = auditLogRepository;
        _ragSettings = ragOptions.Value;
        _logger = logger;
    }

    public async Task<SendMessageResult> Handle(SendMessageCommand request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        var conversation = await _conversationRepository.GetByIdWithMessagesAsync(request.ConversationId, ct);
        if (conversation == null)
            throw new ArgumentException($"Conversation '{request.ConversationId}' not found");
        if (conversation.Status != ConversationStatus.Active)
            throw new InvalidOperationException($"Conversation '{request.ConversationId}' is not active (status: {conversation.Status})");

        var messages = new List<ChatMessage>
        {
            new(MessageRole.System, _defaults.SystemPrompt),
            new(MessageRole.User, request.Content)
        };

        if (!string.IsNullOrWhiteSpace(request.SearchQuery) ||
            !string.IsNullOrWhiteSpace(conversation.CollectionName))
        {
            try
            {
                // 检索词：显式 SearchQuery 优先；否则用消息正文（会话挂 KB 时实现自动接地）。
                var query = (request.SearchQuery ?? request.Content)?.Trim();
                if (!string.IsNullOrWhiteSpace(query))
                {
                    // 检索集合：default 并集（若会话已挂载 KB）其集合名，满足「KB + default 并集」语义。
                    var collections = new List<string> { RoutingConstants.DefaultVectorCollection };
                    if (!string.IsNullOrWhiteSpace(conversation.CollectionName))
                        collections.Add(conversation.CollectionName);

                    var merged = new List<VectorSearchResult>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var collection in collections)
                    {
                        var docs = await _vectorStore.SearchAsync(
                            collection,
                            query,
                            _tenant.GetTenantId(),
                            topK: _ragSettings.DefaultTopK,
                            minScore: _ragSettings.DefaultMinScore,
                            ct: ct);
                        foreach (var doc in docs)
                        {
                            if (seen.Add(doc.Content))
                                merged.Add(doc);
                        }
                    }

                    if (merged.Count > 0)
                    {
                        var context = string.Join("\n", merged.Select(d => d.Content));
                        messages.Insert(1, new ChatMessage(
                            MessageRole.System,
                            $"Context from knowledge base:\n{context}"));
                    }
                }
            }
            catch (Exception ex)
            {
                // RAG 检索失败时降级为不使用上下文，而不是让整条消息 500。
                _logger.LogWarning(ex,
                    "知识库检索失败，降级为不使用上下文（tenant={TenantId}）", _tenant.GetTenantId());
            }
        }

        var routeRequest = new RoutingRequest(
            _tenant.GetTenantId(),
            messages,
            request.Model);

        var response = await _router.RouteAsync(routeRequest, ct);

        var userMsg = new Message(Guid.NewGuid(), MessageRole.User, request.Content);
        var agentMsg = new Message(Guid.NewGuid(), MessageRole.Agent, response.Content, tokenUsage: response.TokenUsage);
        conversation.AddMessage(userMsg);
        conversation.AddMessage(agentMsg);

        var tenantId = _tenant.GetTenantId();
        var auditLog = AuditLog.Record(
            tenantId: tenantId,
            action: Domain.Aggregates.AuditLogs.AuditActionType.SendMessage,
            entity: "Conversation",
            entityId: request.ConversationId,
            details: $"Sent message to conversation {request.ConversationId}");
        _auditLogRepository.Add(auditLog);

        return new SendMessageResult(response.Content, response.ModelId, response.TokenUsage);
    }
}
