using AgentPlatform.Application.Agents.Queries.GetAgent;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class GetAgentQueryHandlerTests
{
    private readonly IAgentRepository _repository = Substitute.For<IAgentRepository>();
    private readonly GetAgentQueryHandler _handler;

    public GetAgentQueryHandlerTests()
    {
        _handler = new GetAgentQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_Should_Return_Agent_When_Found()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent(agentId, "test", new AgentType("developer", "Developer", "Writes code"),
            new ModelEndpoint("openai", "gpt-4o", ""),
            "prompt", Guid.NewGuid());
        _repository.GetByIdAsync(agentId, Arg.Any<CancellationToken>()).Returns(agent);

        var result = await _handler.Handle(new GetAgentQuery(agentId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(agentId, result.Id);
    }

    [Fact]
    public async Task Handle_Should_Return_Null_When_Not_Found()
    {
        var agentId = Guid.NewGuid();
        _repository.GetByIdAsync(agentId, Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var result = await _handler.Handle(new GetAgentQuery(agentId), CancellationToken.None);

        Assert.Null(result);
    }
}
