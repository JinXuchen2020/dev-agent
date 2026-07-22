using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.ApiKeys;

/// <summary>
/// Represents an API key aggregate root for authenticating external requests.
/// Keys are stored encrypted at rest via <c>IAesEncryptor</c> and support
/// versioned rotation, expiration, and revocation for full lifecycle management.
/// </summary>
public sealed class ApiKey : ITenantScoped, IAggregateRoot
{
    /// <summary>Gets the unique identifier of the API key.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the tenant that owns this API key.</summary>
    public Guid TenantId { get; private init; }

    /// <summary>
    /// Gets the encrypted key value (ciphertext produced by <c>IAesEncryptor.Encrypt</c>).
    /// The original plaintext key is never stored in the database.
    /// </summary>
    public string EncryptedKeyHash { get; private set; } = null!;

    /// <summary>
    /// Gets a human-readable prefix (first 8 chars of the plaintext key)
    /// for identification in logs and admin UIs.
    /// </summary>
    public string KeyPrefix { get; private set; } = null!;

    /// <summary>Gets a display name for this key (e.g. "Production CI/CD Key").</summary>
    public string DisplayName { get; private init; } = null!;

    /// <summary>Gets the roles granted by this API key, serialized as a comma-separated list.</summary>
    public string RolesCsv { get; private init; } = string.Empty;

    /// <summary>Gets whether this key is currently active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the key version number. Incremented on each rotation.</summary>
    public int KeyVersion { get; private set; }

    /// <summary>Gets when this key was created.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets when this key expires (null = never expires).</summary>
    public DateTime? ExpiresAt { get; private init; }

    /// <summary>Gets when this key was revoked (null = not revoked).</summary>
    public DateTime? RevokedAt { get; private set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    private ApiKey() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKey"/> class.
    /// </summary>
    /// <param name="id">Unique identifier.</param>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="encryptedKey">Encrypted key value (ciphertext).</param>
    /// <param name="keyPrefix">Human-readable prefix for identification.</param>
    /// <param name="displayName">Display name for the key.</param>
    /// <param name="rolesCsv">Comma-separated role names.</param>
    /// <param name="expiresAt">Optional expiration date.</param>
    public ApiKey(
        Guid id,
        Guid tenantId,
        string encryptedKey,
        string keyPrefix,
        string displayName,
        string rolesCsv,
        DateTime? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("Expiration date must be in the future.", nameof(expiresAt));

        Id = id;
        TenantId = tenantId;
        EncryptedKeyHash = encryptedKey;
        KeyPrefix = keyPrefix;
        DisplayName = displayName;
        RolesCsv = rolesCsv;
        IsActive = true;
        KeyVersion = 1;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the collection of domain events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Clears all pending domain events.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Rotates this key by updating its encrypted value, prefix, and incrementing the version.
    /// The old key's encrypted value is replaced; callers should retain the previous
    /// key for a grace period in a separate store or via a secondary lookup.
    /// </summary>
    /// <param name="newEncryptedKey">The new encrypted key value.</param>
    /// <param name="newKeyPrefix">The new key prefix (first 8 chars of the new plaintext key).</param>
    public void Rotate(string newEncryptedKey, string newKeyPrefix)
    {
        ArgumentNullException.ThrowIfNull(newEncryptedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(newKeyPrefix);
        if (!IsActive)
            throw new InvalidOperationException("Cannot rotate a revoked or inactive API key.");
        EncryptedKeyHash = newEncryptedKey;
        KeyPrefix = newKeyPrefix;
        KeyVersion++;
        RevokedAt = null;
    }

    /// <summary>
    /// Revokes this key, marking it inactive with a timestamp.
    /// </summary>
    public void Revoke()
    {
        IsActive = false;
        RevokedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns the list of roles parsed from <see cref="RolesCsv"/>.
    /// </summary>
    public IReadOnlyList<string> GetRoles()
    {
        if (string.IsNullOrWhiteSpace(RolesCsv))
            return [];

        return RolesCsv
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
