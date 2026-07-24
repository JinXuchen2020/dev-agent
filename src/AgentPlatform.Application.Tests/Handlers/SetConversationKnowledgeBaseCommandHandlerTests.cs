using AgentPlatform.Application.Conversations.Commands.SetConversationKnowledgeBase;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Aggregates.KnowledgeBases;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public sealed class SetConversationKnowledgeBaseCommandHandlerTests
{
    private readonly IConversationRepository _conversationRepository = Substitute.For<IConversationRepository>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly SetConversationKnowledgeBaseCommandHandler _handler;

    public SetConversationKnowledgeBaseCommandHandlerTests()
    {
        _handler = new SetConversationKnowledgeBaseCommandHandler(
            _conversationRepository, _knowledgeBaseRepository, _auditLogRepository);
    }

    [Fact]
    public async Task Handle_Should_Attach_KnowledgeBase_To_Conversation()
    {
        var conversationId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        var kb = KnowledgeBase.Create(_tenantId, "Docs", "", "docs-collection", "model");

        _conversationRepository.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        _knowledgeBaseRepository.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(kb);

        var result = await _handler.Handle(
            new SetConversationKnowledgeBaseCommand(conversationId, kbId, _tenantId), CancellationToken.None);

        Assert.Equal(conversationId, result);
        Assert.Equal(kb.Id, conversation.KnowledgeBaseId);
        Assert.Equal("docs-collection", conversation.CollectionName);
    }

    [Fact]
    public async Task Handle_Should_Reject_CrossTenant_KnowledgeBase()
    {
        var conversationId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        var otherTenantKb = KnowledgeBase.Create(Guid.NewGuid(), "Docs", "", "docs-collection", "model");

        _conversationRepository.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns(conversation);
        _knowledgeBaseRepository.GetByIdAsync(kbId, Arg.Any<CancellationToken>()).Returns(otherTenantKb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(
                new SetConversationKnowledgeBaseCommand(conversationId, kbId, _tenantId), CancellationToken.None));

        Assert.Null(conversation.KnowledgeBaseId);
    }

    [Fact]
    public async Task Handle_Should_Throw_When_Conversation_NotFound()
    {
        var conversationId = Guid.NewGuid();
        _conversationRepository.GetByIdAsync(conversationId, Arg.Any<CancellationToken>()).Returns((Conversation?)null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(
                new SetConversationKnowledgeBaseCommand(conversationId, Guid.NewGuid(), _tenantId),
                CancellationToken.None));
    }
}
