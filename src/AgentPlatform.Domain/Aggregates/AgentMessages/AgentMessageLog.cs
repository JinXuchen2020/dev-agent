using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.AgentMessages;

/// <summary>
/// Persisted record of one inter-agent message (F32 durable log).
/// Write-through on publish: the bus persists before delivering, giving at-least-once
/// semantics with a replayable audit trail. Idempotent consumption is enforced by
/// <see cref="MarkConsumed"/> guarded by <c>ConsumedAt IS NULL</c> at the store level.
/// </summary>
public sealed class AgentMessageLog : IAggregateRoot, ITenantScoped
{
    /// <summary>Gets the message identifier (primary key; equals the in-flight MessageId).</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the workflow this message belongs to.</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>Groups all messages of one negotiation run for trace replay.</summary>
    public Guid CorrelationId { get; private init; }

    /// <summary>Gets the sending agent identifier.</summary>
    public Guid SenderId { get; private init; }

    /// <summary>Gets the receiving agent identifier (<see cref="Guid.Empty"/> means broadcast).</summary>
    public Guid ReceiverId { get; private init; }

    /// <summary>Gets the message type.</summary>
    public AgentMessageType MessageType { get; private init; }

    /// <summary>Gets the JSON payload carried by the message.</summary>
    public string Payload { get; private init; } = null!;

    /// <summary>Gets the negotiation round this message was produced in.</summary>
    public int Round { get; private init; }

    /// <summary>Gets the UTC timestamp when the message was published.</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>Gets the UTC timestamp when the message was consumed, or null while pending.</summary>
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>Gets the tenant that owns this message.</summary>
    public Guid TenantId { get; private init; }

    /// <summary>Gets domain events raised by this aggregate (none — pure log record).</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => Array.Empty<IDomainEvent>();

    /// <summary>Clears pending domain events. No-op: this aggregate raises no events.</summary>
    public void ClearDomainEvents() { }

    private AgentMessageLog() { }

    /// <summary>Initializes a persisted agent message record.</summary>
    public AgentMessageLog(
        Guid messageId, Guid workflowId, Guid correlationId,
        Guid senderId, Guid receiverId,
        AgentMessageType messageType, string payload,
        int round, Guid tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        if (messageId == Guid.Empty) throw new ArgumentException("MessageId must not be empty.", nameof(messageId));

        Id = messageId;
        WorkflowId = workflowId;
        CorrelationId = correlationId;
        SenderId = senderId;
        ReceiverId = receiverId;
        MessageType = messageType;
        Payload = payload;
        Round = round;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the message consumed. Callers must treat "already consumed" as a no-op signal:
    /// the repository's conditional update returns false and the consumer skips processing —
    /// this is what makes redelivery idempotent.
    /// </summary>
    public void MarkConsumed()
    {
        if (ConsumedAt.HasValue)
            return; // already consumed — keep first-consumed timestamp (idempotent)
        ConsumedAt = DateTime.UtcNow;
    }
}