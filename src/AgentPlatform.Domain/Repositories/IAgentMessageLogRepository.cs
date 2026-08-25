using AgentPlatform.Domain.Aggregates.AgentMessages;

namespace AgentPlatform.Domain.Repositories;

/// <summary>
/// Persistence contract for the agent message log (F32). Backs the write-through
/// publish path, unconsumed-message redelivery, and idempotent consumption.
/// </summary>
public interface IAgentMessageLogRepository
{
    /// <summary>Returns true when a message with this id already exists (publish-side dedup).</summary>
    Task<bool> ExistsAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Appends a message to the durable log.</summary>
    void Add(AgentMessageLog message);

    /// <summary>Persists the ConsumedAt stamp of a consumed message.</summary>
    void Update(AgentMessageLog message);

    /// <summary>
    /// Attempts to atomically mark the message consumed. Returns false when it was already
    /// consumed — the caller must then SKIP processing (this is the idempotency gate).
    /// </summary>
    Task<bool> TryMarkConsumedAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Loads all not-yet-consumed messages of a workflow (redelivery source), oldest first.</summary>
    Task<IReadOnlyList<AgentMessageLog>> GetUnconsumedByWorkflowAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>Loads the full message trail of a workflow for trace replay, oldest first.</summary>
    Task<IReadOnlyList<AgentMessageLog>> GetByWorkflowAsync(Guid workflowId, CancellationToken ct = default);
}