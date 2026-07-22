using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.ToolDefinitions;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Domain.Aggregates.Agents;

/// <summary>
/// Represents an agent aggregate root within the multi-agent platform, encapsulating
/// its identity, role, model endpoint, system prompt, associated tools, and lifecycle status.
/// </summary>
public sealed class Agent : ITenantScoped, IAggregateRoot
{
    private readonly List<ToolDefinition> _tools = [];
    private readonly List<string> _skillPackageNames = [];
    private readonly List<string> _mcpServerNames = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the unique identifier of the agent.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets or sets the display name of the agent.
    /// </summary>
    /// <summary>
    /// Gets or sets the display name of the agent.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets the functional role assigned to the agent.
    /// </summary>
    public AgentType Role { get; private init; } = null!;

    /// <summary>
    /// Gets or sets the model endpoint configuration used by the agent for LLM invocations.
    /// </summary>
    public ModelEndpoint ModelEndpoint { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the system prompt that defines the agent's behavior and instructions.
    /// </summary>
    public string SystemPrompt { get; private set; } = null!;

    /// <summary>
    /// Gets a read-only list of tool definitions available to the agent.
    /// </summary>
    public IReadOnlyList<ToolDefinition> Tools => _tools;

    /// <summary>
    /// Gets a read-only list of skill package names associated with the agent.
    /// </summary>
    public IReadOnlyList<string> SkillPackages => _skillPackageNames;

    /// <summary>
    /// Gets a read-only list of MCP server names the agent can connect to.
    /// </summary>
    public IReadOnlyList<string> McpServers => _mcpServerNames;

    /// <summary>
    /// Gets or sets the current operational status of the agent.
    /// </summary>
    public AgentStatus Status { get; private set; }

    /// <summary>
    /// Gets the unique identifier of the tenant that owns this agent.
    /// </summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the agent was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the agent was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate and awaiting dispatch.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Agent() { }

    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all pending domain events from this aggregate after they have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Initializes a new instance of the <see cref="Agent"/> class and raises an
    /// <see cref="Events.AgentCreated"/> domain event.
    /// </summary>
    /// <param name="id">The unique identifier of the agent.</param>
    /// <param name="name">The display name of the agent.</param>
    /// <param name="role">The functional role assigned to the agent.</param>
    /// <param name="modelEndpoint">The model endpoint configuration for LLM invocations.</param>
    /// <param name="systemPrompt">The system prompt defining the agent's behavior.</param>
    /// <param name="tenantId">The unique identifier of the tenant that owns the agent.</param>
    public Agent(Guid id, string name, AgentType role, ModelEndpoint modelEndpoint,
        string systemPrompt, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(modelEndpoint);

        Id = id;
        Name = name;
        Role = role;
        ModelEndpoint = modelEndpoint;
        SystemPrompt = systemPrompt;
        Status = AgentStatus.Active;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
        AddDomainEvent(new Events.AgentCreated(id, name, role.RoleCode, tenantId));
    }

    /// <summary>
    /// Updates the display name of the agent.
    /// </summary>
    /// <param name="name">The new display name for the agent.</param>
    public void UpdateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the model endpoint configuration used by the agent.
    /// </summary>
    /// <param name="endpoint">The new model endpoint configuration.</param>
    public void UpdateModelEndpoint(ModelEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ModelEndpoint = endpoint;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the system prompt that defines the agent's behavior.
    /// </summary>
    /// <param name="prompt">The new system prompt for the agent.</param>
    public void UpdateSystemPrompt(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        SystemPrompt = prompt;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the operational status of the agent.
    /// </summary>
    /// <param name="status">The new status to assign to the agent.</param>
    public void SetStatus(AgentStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Associates a tool definition with the agent, making it available for use.
    /// </summary>
    /// <param name="tool">The tool definition to add.</param>
    public void AddTool(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools.Add(tool);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Associates a skill package with the agent by name.
    /// </summary>
    /// <param name="packageName">The name of the skill package to add.</param>
    public void AddSkillPackage(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        _skillPackageNames.Add(packageName);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Registers an MCP server that the agent can connect to.
    /// </summary>
    /// <param name="serverName">The name of the MCP server to add.</param>
    public void AddMcpServer(string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        _mcpServerNames.Add(serverName);
        UpdatedAt = DateTime.UtcNow;
    }
}
