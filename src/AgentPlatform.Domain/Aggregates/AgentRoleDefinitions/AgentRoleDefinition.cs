using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.AgentRoleDefinitions;

/// <summary>
/// Represents an agent role definition — the single, database-authoritative catalog of agent roles.
/// Both built-in platform roles (seeded by <c>DatabaseInitializer</c>, <see cref="BuiltInRoleCatalog"/>)
/// and tenant-defined custom roles live in this table; <see cref="IsBuiltIn"/> distinguishes them.
/// Each definition specifies the role metadata and the system prompt used when an agent is assigned this role.
/// </summary>
public sealed class AgentRoleDefinition : IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Gets the unique identifier of the role definition.
    /// </summary>
    public Guid Id { get; private init; }

    /// <summary>
    /// Gets the display name of the role (e.g., "Security Auditor").
    /// </summary>
    /// <summary>
    /// Gets the display name of the role (e.g., "Security Auditor").
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Gets the unique code identifying this role (e.g., "security-auditor").
    /// </summary>
    public string RoleCode { get; private init; } = null!;

    /// <summary>
    /// Gets a description of the role's responsibilities.
    /// </summary>
    public string Description { get; private set; } = null!;

    /// <summary>
    /// Gets the system prompt used by agents assigned to this role.
    /// </summary>
    public string SystemPrompt { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether this role is shipped by the platform (built-in)
    /// rather than created by a tenant. Built-in roles are read-only and cannot be deleted.
    /// </summary>
    public bool IsBuiltIn { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the role was created.
    /// </summary>
    public DateTime CreatedAt { get; private init; }

    private AgentRoleDefinition() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRoleDefinition"/> class.
    /// </summary>
    /// <param name="id">The unique identifier of the role definition.</param>
    /// <param name="name">The display name of the role.</param>
    /// <param name="roleCode">The unique code identifying this role.</param>
    /// <param name="description">A description of the role's responsibilities.</param>
    /// <param name="systemPrompt">The system prompt used by agents assigned to this role.</param>
    /// <param name="isBuiltIn">Whether this role is a built-in platform role.</param>
    public AgentRoleDefinition(
        Guid id,
        string name,
        string roleCode,
        string description,
        string systemPrompt,
        bool isBuiltIn = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        Id = id;
        Name = name;
        RoleCode = roleCode;
        Description = description ?? string.Empty;
        SystemPrompt = systemPrompt;
        IsBuiltIn = isBuiltIn;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks this role definition as a built-in platform role (idempotent).
    /// Used by the database initializer to reconcile rows that were seeded before the
    /// <c>IsBuiltIn</c> column existed.
    /// </summary>
    public void MarkAsBuiltIn() => IsBuiltIn = true;

    /// <summary>
    /// Updates the editable metadata of this role definition. The role code is the
    /// immutable key and is never changed by this method.
    /// </summary>
    /// <param name="name">The new display name.</param>
    /// <param name="description">The new description.</param>
    /// <param name="systemPrompt">The new system prompt.</param>
    public void UpdateMetadata(string name, string description, string systemPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        Name = name;
        Description = description ?? string.Empty;
        SystemPrompt = systemPrompt;
    }

    /// <summary>
    /// Gets the collection of domain events raised by this aggregate and awaiting dispatch.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Clears all pending domain events from this aggregate after they have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
