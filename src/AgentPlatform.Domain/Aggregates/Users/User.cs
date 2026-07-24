using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.Users;

/// <summary>
/// Represents a platform user (tenant-scoped) who authenticates with email + password.
/// Passwords are never stored in plaintext — only a PBKDF2 hash produced by
/// <c>IPasswordHasher</c> is persisted in <see cref="PasswordHash"/>.
/// </summary>
public sealed class User : ITenantScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the user.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the tenant that owns this user.</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets the user's email (unique per tenant, used as login name).</summary>
    public string Email { get; private set; } = null!;

    /// <summary>Gets the PBKDF2 password hash (format: $pbkdf2$v=...$salt$hash).</summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>Gets the user's role (e.g. "Admin", "Operator", "User").</summary>
    public string Role { get; private set; } = null!;

    /// <summary>Gets whether this user is active and may authenticate.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when this user was created (UTC).</summary>
    public DateTime CreatedAt { get; private init; }

    private readonly List<IDomainEvent> _domainEvents = [];

    private User() { }

    /// <summary>
    /// Initializes a new user aggregate. Prefer building the PBKDF2
    /// <paramref name="passwordHash"/> via <c>IPasswordHasher</c> before calling.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="email">Login email (unique per tenant).</param>
    /// <param name="passwordHash">PBKDF2 hash of the password (never plaintext).</param>
    /// <param name="role">Role granted to the user.</param>
    public User(Guid id, Guid tenantId, string email, string passwordHash, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        Id = id;
        TenantId = tenantId;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Gets the collection of domain events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Clears all pending domain events.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
