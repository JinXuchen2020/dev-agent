using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.AgentRoleManagement.Commands.DeleteAgentRole;
using AgentPlatform.Application.AgentRoleManagement.Commands.UpdateAgentRoleDefinition;
using AgentPlatform.Application.AgentRoleManagement.Queries.ListAgentRoles;
using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Repositories;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Handlers;

public sealed class AgentRoleManagementHandlersTests
{
    private static AgentRoleDefinition BuiltIn(string code) =>
        new(Guid.NewGuid(), $"name-{code}", code, "desc", "prompt", isBuiltIn: true);

    private static AgentRoleDefinition Custom(string code) =>
        new(Guid.NewGuid(), $"name-{code}", code, "desc", "prompt", isBuiltIn: false);

    [Fact]
    public async Task ListAgentRolesQueryHandler_ReturnsIsBuiltInAndAgentCount()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var roles = new List<AgentRoleDefinition> { BuiltIn("architecture"), Custom("custom-1") };

        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(roles);

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.CountByRoleAsync(tenantId, "architecture", Arg.Any<CancellationToken>()).Returns(3);
        agentRepo.CountByRoleAsync(tenantId, "custom-1", Arg.Any<CancellationToken>()).Returns(0);

        var tenant = Substitute.For<ITenantProvider>();
        tenant.GetTenantId().Returns(tenantId);

        var handler = new ListAgentRolesQueryHandler(roleRepo, agentRepo, tenant);

        // Act
        var result = await handler.Handle(new ListAgentRolesQuery(), CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);
        var builtIn = Assert.Single(result, r => r.RoleCode == "architecture");
        Assert.True(builtIn.IsBuiltIn);
        Assert.Equal(3, builtIn.AgentCount);

        var custom = Assert.Single(result, r => r.RoleCode == "custom-1");
        Assert.False(custom.IsBuiltIn);
        Assert.Equal(0, custom.AgentCount);
    }

    [Fact]
    public async Task UpdateAgentRoleDefinition_UpdatesMetadataAndPreservesBuiltInFlag()
    {
        // Arrange
        var role = BuiltIn("architecture");
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("architecture", Arg.Any<CancellationToken>()).Returns(role);

        var handler = new UpdateAgentRoleDefinitionCommandHandler(roleRepo);

        // Act
        var result = await handler.Handle(
            new UpdateAgentRoleDefinitionCommand("architecture", "新名称", "新描述", "新提示"),
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("新名称", result!.Name);
        Assert.Equal("新描述", result.Description);
        Assert.Equal("新提示", result.SystemPrompt);
        Assert.True(result.IsBuiltIn); // flag preserved, never flipped
        roleRepo.Received().Update(role);
    }

    [Fact]
    public async Task UpdateAgentRoleDefinition_ReturnsNull_WhenRoleMissing()
    {
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("ghost", Arg.Any<CancellationToken>()).Returns((AgentRoleDefinition?)null);

        var handler = new UpdateAgentRoleDefinitionCommandHandler(roleRepo);

        var result = await handler.Handle(
            new UpdateAgentRoleDefinitionCommand("ghost", "x", null, "y"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAgentRole_BuiltIn_ReturnsBuiltInConflict()
    {
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("architecture", Arg.Any<CancellationToken>()).Returns(BuiltIn("architecture"));
        var agentRepo = Substitute.For<IAgentRepository>();

        var handler = new DeleteAgentRoleCommandHandler(roleRepo, agentRepo);

        var outcome = await handler.Handle(new DeleteAgentRoleCommand("architecture"), CancellationToken.None);

        Assert.Equal(AgentRoleDeletionOutcome.BuiltInConflict, outcome);
        roleRepo.DidNotReceive().Remove(Arg.Any<AgentRoleDefinition>());
    }

    [Fact]
    public async Task DeleteAgentRole_InUse_ReturnsInUseConflict()
    {
        var role = Custom("custom-1");
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("custom-1", Arg.Any<CancellationToken>()).Returns(role);

        var referencingAgent = new AgentPlatform.Domain.Aggregates.Agents.Agent(
            Guid.NewGuid(),
            "ref",
            AgentPlatform.Domain.ValueObjects.AgentType.Development,
            new AgentPlatform.Domain.ValueObjects.ModelEndpoint("stub", "stub", "http://localhost"),
            "prompt",
            Guid.NewGuid());

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetByRoleAsync("custom-1", Arg.Any<CancellationToken>())
            .Returns(new List<AgentPlatform.Domain.Aggregates.Agents.Agent> { referencingAgent });

        var handler = new DeleteAgentRoleCommandHandler(roleRepo, agentRepo);

        var outcome = await handler.Handle(new DeleteAgentRoleCommand("custom-1"), CancellationToken.None);

        Assert.Equal(AgentRoleDeletionOutcome.InUseConflict, outcome);
        roleRepo.DidNotReceive().Remove(Arg.Any<AgentRoleDefinition>());
    }

    [Fact]
    public async Task DeleteAgentRole_UnusedCustom_ReturnsDeleted()
    {
        var role = Custom("custom-1");
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("custom-1", Arg.Any<CancellationToken>()).Returns(role);

        var agentRepo = Substitute.For<IAgentRepository>();
        agentRepo.GetByRoleAsync("custom-1", Arg.Any<CancellationToken>())
            .Returns(new List<AgentPlatform.Domain.Aggregates.Agents.Agent>());

        var handler = new DeleteAgentRoleCommandHandler(roleRepo, agentRepo);

        var outcome = await handler.Handle(new DeleteAgentRoleCommand("custom-1"), CancellationToken.None);

        Assert.Equal(AgentRoleDeletionOutcome.Deleted, outcome);
        roleRepo.Received().Remove(role);
    }

    [Fact]
    public async Task DeleteAgentRole_Missing_ReturnsNotFound()
    {
        var roleRepo = Substitute.For<IAgentRoleDefinitionRepository>();
        roleRepo.GetByRoleCodeAsync("ghost", Arg.Any<CancellationToken>()).Returns((AgentRoleDefinition?)null);
        var agentRepo = Substitute.For<IAgentRepository>();

        var handler = new DeleteAgentRoleCommandHandler(roleRepo, agentRepo);

        var outcome = await handler.Handle(new DeleteAgentRoleCommand("ghost"), CancellationToken.None);

        Assert.Equal(AgentRoleDeletionOutcome.NotFound, outcome);
    }
}
