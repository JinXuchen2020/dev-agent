using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.ToolDefinitions;

/// <summary>
/// Represents a tool definition aggregate root that describes a callable tool
/// available to agents, including its source, parameters schema, and handler.
/// </summary>
public sealed class ToolDefinition : ITenantScoped, IAggregateRoot
{
    /// <summary>
    /// Gets the unique identifier of the tool definition.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the unique identifier of the tenant that owns this tool definition.
    /// </summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the human-readable name of the tool.
    /// </summary>
    public string Name { get; private init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets the human-readable description of what the tool does.
    /// </summary>
    public string Description { get; private init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets or sets the JSON schema describing the tool's parameters.
    /// </summary>
    public string ParametersSchema { get; private set; } = null!; // EF Core proxy

    /// <summary>
    /// Gets the name of the handler responsible for executing the tool.
    /// </summary>
    public string HandlerName { get; private init; } = null!; // EF Core proxy

    /// <summary>
    /// Gets or sets a value indicating whether the tool is currently enabled for use.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// Gets the source category of the tool (native, skill package, or MCP server).
    /// </summary>
    public ToolSource Source { get; private init; } = ToolSource.NativeTool;

    /// <summary>
    /// Gets the optional endpoint URL for tools backed by an external service.
    /// </summary>
    public string? EndpointUrl { get; private init; }

    /// <summary>
    /// Gets the optional skill plugin name for tools sourced from a skill package.
    /// </summary>
    public string? SkillPluginName { get; private init; }

    private readonly List<IDomainEvent> _domainEvents = [];

    private ToolDefinition() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDefinition"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the tool definition.</param>
    /// <param name="name">The human-readable name of the tool.</param>
    /// <param name="description">The human-readable description of the tool.</param>
    /// <param name="parametersSchema">The JSON schema describing the tool's parameters.</param>
    /// <param name="handlerName">The name of the handler responsible for executing the tool.</param>
    /// <param name="tenantId">The unique identifier of the tenant that owns this tool definition.</param>
    /// <param name="source">The source category of the tool. Defaults to <see cref="ToolSource.NativeTool"/>.</param>
    /// <param name="endpointUrl">The optional endpoint URL for external tools.</param>
    /// <param name="skillPluginName">The optional skill plugin name for skill-sourced tools.</param>
    public ToolDefinition(Guid id, string name, string description,
        string parametersSchema, string handlerName, Guid tenantId,
        ToolSource source = ToolSource.NativeTool,
        string? endpointUrl = null, string? skillPluginName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        Id = id;
        Name = name;
        Description = description;
        ParametersSchema = parametersSchema;
        HandlerName = handlerName;
        TenantId = tenantId;
        IsEnabled = true;
        Source = source;
        EndpointUrl = endpointUrl;
        SkillPluginName = skillPluginName;
    }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate and awaiting dispatch.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Clears all pending domain events from this aggregate after they have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Enables the tool for use by agents.
    /// </summary>
    public void Enable()
    {
        IsEnabled = true;
    }

    /// <summary>
    /// Disables the tool, preventing its use by agents.
    /// </summary>
    public void Disable()
    {
        IsEnabled = false;
    }

    /// <summary>
    /// Updates the JSON schema describing the tool's parameters.
    /// </summary>
    /// <param name="schema">The new JSON parameters schema.</param>
    public void UpdateParametersSchema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ParametersSchema = schema;
    }
}
