namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// In-process message bus for inter-agent collaboration (F32, blueprint D2: Channel&lt;T&gt; start).
/// Publish is write-through (durable log first, then in-memory inbox delivery); consumption is
/// idempotent via the log's conditional consume gate. Scoped lifetime — one bus per workflow run.
/// </summary>
public interface IAgentMessageBus
{
    /// <summary>
    /// Persists the message (dedup by <paramref name="message"/>.MessageId — a duplicate publish
    /// is a no-op) then enqueues it into the receiver's inbox. Broadcast receiver
    /// (<see cref="Guid.Empty"/>) fans out to every registered inbox.
    /// </summary>
    Task PublishAsync(AgentMessage message, Guid tenantId, CancellationToken ct = default);

    /// <summary>Drains and returns all currently pending inbox messages for the receiver.</summary>
    IAsyncEnumerable<AgentMessage> ReadAllAsync(Guid receiverId, CancellationToken ct = default);

    /// <summary>
    /// Re-publishes durable messages of the workflow that were never consumed (crash/redelivery
    /// path). Returns the number of messages re-enqueued. Idempotency on the consumer side
    /// guarantees reprocessed duplicates are skipped.
    /// </summary>
    Task<int> RepublishUnconsumedAsync(Guid workflowId, Guid tenantId, CancellationToken ct = default);
}