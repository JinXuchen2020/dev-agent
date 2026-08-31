using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Aggregates.AgentConfigurations.Events;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.ValueObjects;

namespace AgentPlatform.Domain.Aggregates.AgentConfigurations;

/// <summary>
/// Represents an agent configuration definition aggregate root, encapsulating
/// the YAML-based configuration content, versioning, lifecycle status, and tenant scoping.
/// Supports version management for tracking changes over time.
/// </summary>
public sealed class AgentConfiguration : ITenantScoped, IWorkspaceScoped, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the unique identifier of the configuration.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the display name of the configuration.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets an optional description of the configuration's purpose.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the YAML content defining the agent configuration.
    /// </summary>
    public string YamlContent { get; private set; } = null!;

    /// <summary>
    /// Gets the current semantic version of the configuration.
    /// </summary>
    public ConfigurationVersion Version { get; private set; } = null!;

    /// <summary>
    /// Gets the role code this configuration is intended for, if any.
    /// </summary>
    public string? AgentTypeCode { get; private set; }

    /// <summary>
    /// Gets the current lifecycle status of the configuration.
    /// </summary>
    public AgentConfigurationStatus Status { get; private set; }

    /// <summary>
    /// Gets the tenant that owns this configuration.
    /// </summary>
    public Guid TenantId { get; private init; }
    public Guid WorkspaceId { get; private init; }

    /// <summary>
    /// Gets the UTC timestamp when the configuration was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the configuration was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private AgentConfiguration() { }

    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all pending domain events after they have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentConfiguration"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the configuration.</param>
    /// <param name="name">The display name of the configuration.</param>
    /// <param name="yamlContent">The YAML content defining the agent configuration.</param>
    /// <param name="tenantId">The tenant that owns this configuration.</param>
    /// <param name="version">The initial semantic version. Defaults to 1.0.0.</param>
    /// <param name="description">An optional description of the configuration's purpose.</param>
    /// <param name="agentTypeCode">An optional role code this configuration is intended for.</param>
    public AgentConfiguration(
        Guid id,
        string name,
        string yamlContent,
        Guid tenantId,
        ConfigurationVersion? version = null,
        string? description = null,
        string? agentTypeCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);

        Id = id;
        Name = name;
        YamlContent = yamlContent;
        TenantId = tenantId;
        Version = version ?? ConfigurationVersion.Initial;
        Description = description;
        AgentTypeCode = agentTypeCode;
        Status = AgentConfigurationStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;

        AddDomainEvent(new AgentConfigurationCreated(
            id, name, Version.ToString(), tenantId));
    }

    /// <summary>
    /// Updates the YAML content and bumps the version.
    /// </summary>
    /// <param name="newYamlContent">The new YAML configuration content.</param>
    /// <param name="changeLog">A description of the changes in this version.</param>
    /// <param name="versionBump">The type of version bump to apply.</param>
    public void UpdateContent(
        string newYamlContent,
        string? changeLog = null,
        VersionBump versionBump = VersionBump.Patch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newYamlContent);

        var previousVersion = Version.ToString();
        var newVersion = versionBump switch
        {
            VersionBump.Major => new ConfigurationVersion(
                Version.Major + 1, 0, 0, changeLog),
            VersionBump.Minor => new ConfigurationVersion(
                Version.Major, Version.Minor + 1, 0, changeLog),
            _ => new ConfigurationVersion(
                Version.Major, Version.Minor, Version.Patch + 1, changeLog)
        };

        YamlContent = newYamlContent;
        Version = newVersion;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new AgentConfigurationUpdated(
            Id, newVersion.ToString(), previousVersion, TenantId));
    }

    /// <summary>
    /// Updates the display name of the configuration.
    /// </summary>
    /// <param name="name">The new display name.</param>
    public void UpdateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the description of the configuration.
    /// </summary>
    /// <param name="description">The new description value.</param>
    public void UpdateDescription(string? description)
    {
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the lifecycle status of the configuration.
    /// </summary>
    /// <param name="status">The new status to assign.</param>
    public void SetStatus(AgentConfigurationStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Activates the configuration, transitioning it from Draft to Active.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the configuration is not in Draft status.</exception>
    public void Activate()
    {
        if (Status != AgentConfigurationStatus.Draft)
            throw new InvalidOperationException(
                $"Cannot activate a configuration with status '{Status}'. Only Draft configurations can be activated.");

        Status = AgentConfigurationStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Archives the configuration, marking it as no longer in active use.
    /// </summary>
    public void Archive()
    {
        Status = AgentConfigurationStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Specifies the type of version increment to apply when updating a configuration.
/// </summary>
public enum VersionBump
{
    /// <summary>Increment the patch version (bug fixes, minor changes).</summary>
    Patch = 0,

    /// <summary>Increment the minor version (backward-compatible additions).</summary>
    Minor = 1,

    /// <summary>Increment the major version (breaking changes).</summary>
    Major = 2
}
