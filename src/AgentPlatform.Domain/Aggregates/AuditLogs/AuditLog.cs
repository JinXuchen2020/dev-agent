using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.AuditLogs;

/// <summary>
/// Represents an audit log entry for tracking key operations. Append-only — no delete method.
/// </summary>
public sealed class AuditLog : IAggregateRoot
{
    public Guid Id { get; private init; }
    public Guid TenantId { get; private init; }
    public string? UserId { get; private init; }
    public AuditActionType Action { get; private init; }
    public string Entity { get; private init; } = null!;
    public Guid? EntityId { get; private init; }
    public string? Details { get; private init; }
    public DateTime CreatedAt { get; private init; }

    private AuditLog() { }

    /// <summary>
    /// Gets the collection of domain events. AuditLog is append-only and does not raise events —
    /// retained for <see cref="IAggregateRoot"/> contract compliance.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <summary>
    /// Clears all pending domain events. No-op — AuditLog does not raise events.
    /// </summary>
    public void ClearDomainEvents() { }

    public static AuditLog Record(
        Guid tenantId,
        AuditActionType action,
        string entity,
        string? userId = null,
        Guid? entityId = null,
        string? details = null)
    {
        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Types of auditable actions.
/// </summary>
public enum AuditActionType
{
    CreateAgent,
    DeleteAgent,
    RunWorkflow,
    PauseWorkflow,
    ResumeWorkflow,
    RollbackWorkflow,
    SendMessage,
    CreateConversation,
    UpdateConversation,
    DeleteConversation,
    UpdateConfiguration,
    DeleteConfiguration,
    CreateAgentRole,
    DeleteAgentRole,

    /// <summary>An API key rotation was performed.</summary>
    KeyRotation,

    /// <summary>An API key was used to authenticate a request.</summary>
    KeyUsed,

    /// <summary>An API key was revoked (e.g. after expiry).</summary>
    KeyRevoked
}
