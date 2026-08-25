using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// One inter-agent message flowing over the <see cref="IAgentMessageBus"/> (F32).
/// Immutable value; durable form is <c>AgentMessageLog</c>.
/// </summary>
/// <param name="MessageId">Unique message identifier (also the durable log primary key / dedup key).</param>
/// <param name="WorkflowId">The workflow run this message belongs to.</param>
/// <param name="CorrelationId">Groups all messages of one negotiation run for trace replay.</param>
/// <param name="SenderId">Sending agent id.</param>
/// <param name="ReceiverId">Receiving agent id (<see cref="Guid.Empty"/> = broadcast, reserved).</param>
/// <param name="Type">Message type.</param>
/// <param name="Payload">JSON payload.</param>
/// <param name="Round">Negotiation round that produced the message.</param>
public sealed record AgentMessage(
    Guid MessageId,
    Guid WorkflowId,
    Guid CorrelationId,
    Guid SenderId,
    Guid ReceiverId,
    AgentMessageType Type,
    string Payload,
    int Round)
{
    /// <summary>UTC publish timestamp (set at construction time).</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}