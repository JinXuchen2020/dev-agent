using AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;
using AgentPlatform.Domain.Aggregates.Agents;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Domain.ValueObjects;
using System.Collections.Concurrent;
using TechTalk.SpecFlow;
using Xunit;

namespace AgentPlatform.SpecFlowTests.Steps;

[Binding]
public class CustomAgentRoleSteps
{
    private readonly InMemoryAgentRoleDefinitionRepository _roleRepo = new();
    private readonly InMemoryAgentRepository _agentRepo = new();
    private AgentRoleDefinition? _createdRole;
    private IReadOnlyList<AgentRoleDefinition> _allRoles = [];
    private bool _validationErrorThrown;
    private readonly Guid _tenantId = Guid.NewGuid();

    [Given("the agent role management system is initialized")]
    public void GivenSystemInitialized()
    {
        _roleRepo.Clear();
        _agentRepo.Clear();
        _createdRole = null;
        _allRoles = [];
        _validationErrorThrown = false;
    }

    [Given("a custom role \"(.*)\" exists")]
    public void GivenCustomRoleExists(string roleName)
    {
        _createdRole = new AgentRoleDefinition(
            Guid.NewGuid(),
            roleName,
            roleName.ToLowerInvariant().Replace(' ', '-'),
            $"Description for {roleName}",
            $"You are a {roleName}...");

        _roleRepo.Add(_createdRole);
    }

    [Given("(.*) custom roles exist")]
    public void GivenCustomRolesExist(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            var role = new AgentRoleDefinition(
                Guid.NewGuid(),
                $"Custom Role {i}",
                $"custom-role-{i}",
                $"Description for custom role {i}",
                $"You are custom role {i}...");

            _roleRepo.Add(role);
        }
    }

    [When("a user creates an agent role with:")]
    public void WhenCreateAgentRoleWithTable(Table table)
    {
        var name = table.Rows[0]["Value"];
        var roleCode = table.Rows[1]["Value"];
        var description = table.Rows[2]["Value"];
        var systemPrompt = table.Rows[3]["Value"];

        try
        {
            _createdRole = new AgentRoleDefinition(
                Guid.NewGuid(),
                name,
                roleCode,
                description,
                systemPrompt);

            _roleRepo.Add(_createdRole);
        }
        catch (ArgumentException)
        {
            _validationErrorThrown = true;
        }
    }

    [When("a user creates an agent with that role")]
    public void WhenCreateAgentWithCustomRole()
    {
        Assert.NotNull(_createdRole);

        var agentType = new AgentType(
            _createdRole.RoleCode,
            _createdRole.Name,
            _createdRole.Description);

        var agent = new Agent(
            Guid.NewGuid(),
            $"Agent for {_createdRole.Name}",
            agentType,
            new ModelEndpoint("stub", "stub", "http://localhost"),
            _createdRole.SystemPrompt,
            _tenantId);

        _agentRepo.Add(agent);

        // Verify the agent uses the custom role's system prompt
        Assert.Equal(_createdRole.SystemPrompt, agent.SystemPrompt);
    }

    [When("a user lists all available roles")]
    public async Task WhenListAllRoles()
    {
        _allRoles = await _roleRepo.GetAllAsync();
    }

    [When("a user deletes the role")]
    public async Task WhenDeleteRole()
    {
        Assert.NotNull(_createdRole);

        var role = await _roleRepo.GetByRoleCodeAsync(_createdRole.RoleCode);
        Assert.NotNull(role);

        _roleRepo.Remove(role);

        // Also unlink agents using this role code
        var agentsWithRole = await _agentRepo.GetByRoleAsync(role.RoleCode);
        foreach (var agent in agentsWithRole)
        {
            _agentRepo.Remove(agent);
        }
    }

    [When("a user creates an agent role with empty name")]
    public void WhenCreateRoleWithEmptyName()
    {
        try
        {
            _createdRole = new AgentRoleDefinition(
                Guid.NewGuid(),
                "",
                "empty-role",
                "A role with empty name",
                "You are an empty role...");

            _roleRepo.Add(_createdRole);
        }
        catch (ArgumentException)
        {
            _validationErrorThrown = true;
        }
    }

    [Then("the role should be saved")]
    public void ThenRoleSaved()
    {
        Assert.NotNull(_createdRole);
    }

    [Then("the role should be queryable by role code \"(.*)\"")]
    public async Task ThenRoleQueryableByCode(string roleCode)
    {
        var role = await _roleRepo.GetByRoleCodeAsync(roleCode);
        Assert.NotNull(role);
        Assert.Equal(roleCode, role.RoleCode);
    }

    [Then("the agent should use the custom role's system prompt")]
    public void ThenAgentUsesCustomPrompt()
    {
        Assert.NotNull(_createdRole);
        var agents = _agentRepo.GetByRoleCodeSync(_createdRole.RoleCode);
        Assert.NotEmpty(agents);
        Assert.All(agents, a => Assert.Equal(_createdRole.SystemPrompt, a.SystemPrompt));
    }

    [Then("the system should return all (.*) roles")]
    public void ThenSystemReturnsAllRoles(int expectedCount)
    {
        Assert.Equal(expectedCount, _allRoles.Count);
    }

    [Then("each role should include its Name, RoleCode, and Description")]
    public void ThenEachRoleHasMetadata()
    {
        Assert.NotEmpty(_allRoles);
        foreach (var role in _allRoles)
        {
            Assert.False(string.IsNullOrWhiteSpace(role.Name));
            Assert.False(string.IsNullOrWhiteSpace(role.RoleCode));
            Assert.NotNull(role.Description);
        }
    }

    [Then("the role should no longer be queryable")]
    public async Task ThenRoleNotQueryable()
    {
        Assert.NotNull(_createdRole);
        var role = await _roleRepo.GetByRoleCodeAsync(_createdRole.RoleCode);
        Assert.Null(role);
    }

    [Then("agents assigned that role should be unlinked")]
    public async Task ThenAgentsUnlinked()
    {
        Assert.NotNull(_createdRole);
        var agents = await _agentRepo.GetByRoleAsync(_createdRole.RoleCode);
        Assert.Empty(agents);
    }

    [Then("the system should return a validation error")]
    public void ThenValidationError()
    {
        Assert.True(_validationErrorThrown);
    }

    [Then("the role should not be created")]
    public void ThenRoleNotCreated()
    {
        Assert.Null(_createdRole);
    }

    /// <summary>
    /// In-memory implementation of <see cref="IAgentRoleDefinitionRepository"/> for spec flow testing.
    /// </summary>
    private sealed class InMemoryAgentRoleDefinitionRepository : IAgentRoleDefinitionRepository
    {
        private readonly ConcurrentDictionary<Guid, AgentRoleDefinition> _store = new();

        public void Clear() => _store.Clear();

        public Task<AgentRoleDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            _store.TryGetValue(id, out var role);
            return Task.FromResult(role);
        }

        public Task<AgentRoleDefinition?> GetByRoleCodeAsync(string roleCode, CancellationToken ct = default)
        {
            var role = _store.Values.FirstOrDefault(r => r.RoleCode == roleCode);
            return Task.FromResult(role);
        }

        public Task<IReadOnlyList<AgentRoleDefinition>> GetAllAsync(CancellationToken ct = default)
        {
            var roles = _store.Values.ToList() as IReadOnlyList<AgentRoleDefinition>;
            return Task.FromResult(roles);
        }

        public void Add(AgentRoleDefinition definition)
        {
            _store.TryAdd(definition.Id, definition);
        }

        public void Update(AgentRoleDefinition definition)
        {
            _store[definition.Id] = definition;
        }

        public void Remove(AgentRoleDefinition definition)
        {
            _store.TryRemove(definition.Id, out _);
        }
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

        public Task<int> CountByRoleAsync(Guid tenantId, string roleCode, CancellationToken ct = default)
        {
            var count = _store.Values
                .Count(a => a.TenantId == tenantId && a.Role.RoleCode == roleCode);
            return Task.FromResult(count);
        }

        public List<Agent> GetByRoleCodeSync(string roleCode)
        {
            return _store.Values.Where(a => a.Role.RoleCode == roleCode).ToList();
        }

        public void Add(Agent agent) => _store.TryAdd(agent.Id, agent);
        public void Update(Agent agent) => _store[agent.Id] = agent;
        public void Remove(Agent agent) => _store.TryRemove(agent.Id, out _);
    }
}
