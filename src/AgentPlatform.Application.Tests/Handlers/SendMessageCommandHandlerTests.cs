using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Conversations.Commands.SendMessage;
using AgentPlatform.Application.Routing;
using AgentPlatform.Application.Routing.Services;
using AgentPlatform.Domain.Aggregates.Conversations;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class SendMessageCommandHandlerTests
{
    private readonly IConversationRepository _conversationRepository = Substitute.For<IConversationRepository>();
    private readonly IModelRouter _router = Substitute.For<IModelRouter>();
    private readonly IVectorStore _vectorStore = Substitute.For<IVectorStore>();
    private readonly IOptions<ModelDefaults> _defaults = Options.Create(new ModelDefaults
    {
        SystemPrompt = "You are a test assistant."
    });
    private readonly ITenantProvider _tenant = Substitute.For<ITenantProvider>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IOptions<RagSettings> _ragOptions = Options.Create(new RagSettings());
    private readonly ILogger<SendMessageCommandHandler> _logger = Substitute.For<ILogger<SendMessageCommandHandler>>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly SendMessageCommandHandler _handler;

    public SendMessageCommandHandlerTests()
    {
        _tenant.GetTenantId().Returns(_tenantId);
        _handler = new SendMessageCommandHandler(
            _conversationRepository, _router, _vectorStore, _defaults, _tenant,
            _auditLogRepository, _ragOptions, _logger);
    }

    [Fact]
    public async Task Handle_Should_Return_Router_Response()
    {
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Hello!", new TokenUsage(10, 5), "gpt-4o", "stop"));

        var command = new SendMessageCommand(conversationId, "Hi there");
        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal("Hello!", result.Reply);
        Assert.Equal("gpt-4o", result.ModelId);
        Assert.Equal(10, result.TokenUsage!.PromptTokens);
        Assert.Equal(5, result.TokenUsage.CompletionTokens);
    }

    [Fact]
    public async Task Handle_Should_Throw_On_Empty_Content()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), "");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Should_Throw_On_Null_Content()
    {
        var command = new SendMessageCommand(Guid.NewGuid(), null!);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Should_Throw_When_Conversation_Not_Found()
    {
        var conversationId = Guid.NewGuid();
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var command = new SendMessageCommand(conversationId, "Hello");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Should_Add_Messages_To_Conversation()
    {
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Reply", null, "model-1", "stop"));

        var command = new SendMessageCommand(conversationId, "Test message");
        await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(2, conversation.Messages.Count);
        Assert.Equal(MessageRole.User, conversation.Messages[0].Role);
        Assert.Equal("Test message", conversation.Messages[0].Content);
        Assert.Equal(MessageRole.Agent, conversation.Messages[1].Role);
        Assert.Equal("Reply", conversation.Messages[1].Content);
    }

    [Fact]
    public async Task Handle_Should_Use_Vector_Context_When_SearchQuery_Provided()
    {
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _vectorStore.SearchAsync(
                RoutingConstants.DefaultVectorCollection,
                "search term",
                _tenantId,
                Arg.Any<int>(),
                Arg.Any<double?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>
            {
                new("doc-1", "Relevant document content", 0.95)
            });
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Answer", null, "model-1", "stop"));

        var command = new SendMessageCommand(conversationId, "Question", SearchQuery: "search term");
        await _handler.Handle(command, CancellationToken.None);

        await _router.Received(1).RouteAsync(
            Arg.Is<RoutingRequest>(r =>
                r.Messages.Any(m =>
                    m.Role == MessageRole.System &&
                    m.Content.Contains("Relevant document content"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Skip_Vector_Search_When_No_Results()
    {
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _vectorStore.SearchAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
                Arg.Any<int>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new List<VectorSearchResult>());
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Answer", null, "model-1", "stop"));

        var command = new SendMessageCommand(conversationId, "Question", SearchQuery: "search term");
        await _handler.Handle(command, CancellationToken.None);

        await _router.Received(1).RouteAsync(
            Arg.Is<RoutingRequest>(r =>
                r.Messages.Count(m => m.Role == MessageRole.System) == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Pass_Preferred_Model_To_Router()
    {
        var conversationId = Guid.NewGuid();
        var conversation = new Conversation(conversationId, _tenantId);
        _conversationRepository.GetByIdWithMessagesAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _router.RouteAsync(Arg.Any<RoutingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ModelResponse("Answer", null, "claude-3", "stop"));

        var command = new SendMessageCommand(conversationId, "Hi", Model: "claude-3");
        await _handler.Handle(command, CancellationToken.None);

        await _router.Received(1).RouteAsync(
            Arg.Is<RoutingRequest>(r => r.PreferredModel == "claude-3"),
            Arg.Any<CancellationToken>());
    }
}
