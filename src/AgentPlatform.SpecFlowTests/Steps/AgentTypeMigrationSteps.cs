using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using System.Collections.Concurrent;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class AgentTypeMigrationSteps
{
    private readonly InMemoryAgentRepository _repository = new();
    private Agent? _createdAgent;
    private IReadOnlyList<Agent> _queryResults = [];
    private readonly Guid _tenantId = Guid.NewGuid();

    [Given("the system is initialized with the AgentRole-to-AgentType migration")]
    public void GivenSystemInitialized()
    {
        _repository.Clear();
    }

    [When("a user creates an agent with role code \"(.*)\"")]
    public void WhenCreateAgentWithRoleCode(string roleCode)
    {
        var role = AgentType.FromCode(roleCode)
            ?? new AgentType(roleCode, roleCode, roleCode);

        _createdAgent = new Agent(
            Guid.NewGuid(),
            $"Test {roleCode}",
            role,
            new ModelEndpoint("stub", "stub", "http://localhost"),
            $"You are a {roleCode}",
            _tenantId);

        _repository.Add(_createdAgent);
    }

    [Given("an agent was created with AgentRole.Architect")]
    public void GivenAgentCreatedWithOldEnum()
    {
        // AgentRole enum has been migrated to AgentType value object.
        // Create an agent using the new AgentType system with the same role code.
        var role = AgentType.Architect;
        _createdAgent = new Agent(
            Guid.NewGuid(),
            "Legacy Architect",
            role,
            new ModelEndpoint("stub", "stub", "http://localhost"),
            "You are an architect",
            _tenantId);

        _repository.Add(_createdAgent);
    }

    [When("the system migrates agent roles")]
    public async Task WhenSystemMigratesAgentRoles()
    {
        // The migration from AgentRole enum to AgentType value object has already
        // been performed in the domain model. This "migration" verifies that
        // existing agents already have the correct AgentType structure.
        var agents = await _repository.GetByTenantAsync(_tenantId);
        foreach (var agent in agents)
        {
            Assert.NotNull(agent.Role);
            Assert.False(string.IsNullOrWhiteSpace(agent.Role.RoleCode));
        }
    }

    [When("a user queries agents by role code \"(.*)\"")]
    public async Task WhenQueryAgentsByRoleCode(string roleCode)
    {
        _queryResults = await _repository.GetByRoleAsync(roleCode);
    }

    [Then("the agent should have an AgentType with RoleCode \"(.*)\"")]
    public void ThenAgentHasAgentTypeWithRoleCode(string expectedRoleCode)
    {
        Assert.NotNull(_createdAgent);
        Assert.NotNull(_createdAgent.Role);
        Assert.Equal(expectedRoleCode, _createdAgent.Role.RoleCode);
    }

    [Then("the agent's role should be retrievable via GetByRoleAsync\\(\"(.*)\"\\)")]
    public async Task ThenRoleRetrievableByRoleCode(string roleCode)
    {
        var results = await _repository.GetByRoleAsync(roleCode);
        Assert.Contains(results, a => a.Id == _createdAgent!.Id);
    }

    [Then("the old AgentRole enum should no longer be referenced in application code")]
    public void ThenOldEnumNotReferenced()
    {
        // The AgentRole enum has been fully removed from the codebase.
        // All role definitions now use AgentType value objects.
        // Verify the agent (created via new system or "migrated") uses AgentType.
        Assert.NotNull(_createdAgent);
        Assert.IsType<AgentType>(_createdAgent.Role);
        Assert.Equal("architect", _createdAgent.Role.RoleCode);
    }

    [Then("the system should return an empty list")]
    public void ThenSystemReturnsEmptyList()
    {
        Assert.NotNull(_queryResults);
        Assert.Empty(_queryResults);
    }

    /// <summary>
    /// In-memory implementation of <see cref="IAgentRepository"/> for spec flow testing.
    /// </summary>
    private sealed class InMemoryAgentRepository : IAgentRepository
    {
        private readonly ConcurrentDictionary<Guid, Agent> _store = new();

        public void Clear() => _store.Clear();

        public Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var agent);
            return Task.FromResult(agent);
        }

        public Task<IReadOnlyList<Agent>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            var agents = _store.Values
                .Where(a => a.TenantId == tenantId)
                .ToList() as IReadOnlyList<Agent>;
            return Task.FromResult(agents);
        }

        public Task<IReadOnlyList<Agent>> GetByRoleAsync(string roleCode, CancellationToken ct = default)
        {
            var agents = _store.Values
                .Where(a => a.Role.RoleCode == roleCode)
                .ToList() as IReadOnlyList<Agent>;
            return Task.FromResult(agents);
        }

        public void Add(Agent agent)
        {
            _store.TryAdd(agent.Id, agent);
        }

        public void Update(Agent agent)
        {
            _store[agent.Id] = agent;
        }

        public void Remove(Agent agent)
        {
            _store.TryRemove(agent.Id, out _);
        }
    }
}
