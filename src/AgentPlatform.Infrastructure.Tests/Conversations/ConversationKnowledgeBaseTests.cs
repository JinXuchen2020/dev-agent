using AgentPlatform.Domain.Aggregates.Conversations;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Conversations;

/// <summary>
/// 覆盖会话挂知识库（conversation-kb-linkage）：Attach/Detach 方法、空值/空集合名校验、
/// 以及重新挂载覆盖旧值。
/// </summary>
public sealed class ConversationKnowledgeBaseTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void Attach_SetsKnowledgeBaseId_And_CollectionName()
    {
        var conversation = new Conversation(Guid.NewGuid(), _tenantId);
        var kbId = Guid.NewGuid();

        conversation.AttachKnowledgeBase(kbId, "my-kb-collection");

        Assert.Equal(kbId, conversation.KnowledgeBaseId);
        Assert.Equal("my-kb-collection", conversation.CollectionName);
    }

    [Fact]
    public void Detach_ClearsKnowledgeBaseLink()
    {
        var conversation = new Conversation(Guid.NewGuid(), _tenantId);
        conversation.AttachKnowledgeBase(Guid.NewGuid(), "my-kb-collection");

        conversation.DetachKnowledgeBase();

        Assert.Null(conversation.KnowledgeBaseId);
        Assert.Null(conversation.CollectionName);
    }

    [Fact]
    public void Attach_Rejects_EmptyKnowledgeBaseId()
    {
        var conversation = new Conversation(Guid.NewGuid(), _tenantId);

        Assert.Throws<ArgumentException>(() =>
            conversation.AttachKnowledgeBase(Guid.Empty, "col"));
    }

    [Fact]
    public void Attach_Rejects_EmptyCollectionName()
    {
        var conversation = new Conversation(Guid.NewGuid(), _tenantId);

        Assert.Throws<ArgumentException>(() =>
            conversation.AttachKnowledgeBase(Guid.NewGuid(), "   "));
        Assert.Throws<ArgumentException>(() =>
            conversation.AttachKnowledgeBase(Guid.NewGuid(), null!));
    }

    [Fact]
    public void Attach_Replaces_PreviousLink()
    {
        var conversation = new Conversation(Guid.NewGuid(), _tenantId);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        conversation.AttachKnowledgeBase(first, "first-collection");
        conversation.AttachKnowledgeBase(second, "second-collection");

        Assert.Equal(second, conversation.KnowledgeBaseId);
        Assert.Equal("second-collection", conversation.CollectionName);
    }
}
