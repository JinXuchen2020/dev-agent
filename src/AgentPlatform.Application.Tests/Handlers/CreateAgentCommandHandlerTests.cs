using AgentPlatform.Application.Agents.Commands.CreateAgent;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public class CreateAgentCommandHandlerTests
{
    private readonly IAgentRepository _repository = Substitute.For<IAgentRepository>();
    private readonly CreateAgentCommandHandler _handler;

    public CreateAgentCommandHandlerTests()
    {
        _handler = new CreateAgentCommandHandler(_repository);
    }

    [Fact]
    public async Task Handle_Should_Create_Agent_With_Given_Values()
    {
        var command = new CreateAgentCommand(
            "test-agent",
            "developer",
            "openai",
            "gpt-4o",
            "https://api.openai.com/v1",
            "You are a coder.",
            Guid.NewGuid());

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.RoleCode, result.Role.RoleCode);
        Assert.Equal(command.ModelProvider, result.ModelEndpoint.Provider);
        Assert.Equal(command.ModelName, result.ModelEndpoint.ModelName);
        Assert.Equal(command.SystemPrompt, result.SystemPrompt);
        Assert.Equal(command.TenantId, result.TenantId);
        Assert.Equal(AgentStatus.Active, result.Status);
    }

    [Fact]
    public async Task Handle_Should_Add_Agent_To_Repository()
    {
        var command = new CreateAgentCommand(
            "test-agent",
            "developer",
            "openai",
            "gpt-4o",
            "https://api.openai.com/v1",
            "You are a coder.",
            Guid.NewGuid());

        await _handler.Handle(command, CancellationToken.None);

        _repository.Received(1).Add(Arg.Is<Agent>(a => a.Name == command.Name));
    }

    [Fact]
    public async Task Handle_Should_Throw_On_Empty_Name()
    {
        var command = new CreateAgentCommand(
            "",
            "developer",
            "openai",
            "gpt-4o",
            "https://api.openai.com/v1",
            "You are a coder.",
            Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(command, CancellationToken.None));
    }
}
