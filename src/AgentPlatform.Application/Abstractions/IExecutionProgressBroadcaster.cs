using System.Threading.Channels;

namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Represents a progress event emitted during workflow execution, serialized as SSE.
/// </summary>
/// <param name="Type">The event type: workflow_started, step_completed, step_failed, workflow_completed, workflow_rolledback.</param>
/// <param name="WorkflowId">The workflow identifier.</param>
/// <param name="ExecutionLogId">The execution log identifier, if available.</param>
/// <param name="StepName">The step name, if applicable.</param>
/// <param name="StepOrder">The step order, if applicable.</param>
/// <param name="Status">The current status text.</param>
/// <param name="Result">The step result, if applicable.</param>
/// <param name="ErrorDetail">The error detail, if applicable.</param>
/// <param name="Timestamp">The UTC timestamp of the event.</param>
public sealed record ExecutionProgressEvent(
    string Type,
    Guid WorkflowId,
    Guid? ExecutionLogId,
    string? StepName,
    int? StepOrder,
    string Status,
    string? Result,
    string? ErrorDetail,
    DateTime Timestamp);

/// <summary>
/// Provides real-time broadcasting of workflow execution progress events
/// to connected SSE clients via <see cref="System.Threading.Channels.Channel{T}"/>.
/// </summary>
public interface IExecutionProgressBroadcaster
{
    /// <summary>
    /// Publishes a progress event to all subscribers watching the specified workflow.
    /// </summary>
    /// <param name="workflowId">The workflow identifier to publish to.</param>
    /// <param name="event">The progress event to publish.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask PublishAsync(Guid workflowId, ExecutionProgressEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to progress events for the specified workflow.
    /// Returns a subscriber ID and a <see cref="ChannelReader{T}"/> that yields events as they occur.
    /// Call <see cref="Unsubscribe"/> with the subscriber ID to clean up.
    /// </summary>
    /// <param name="workflowId">The workflow identifier to subscribe to.</param>
    /// <returns>A tuple containing the subscriber ID and a channel reader for the event stream.</returns>
    (Guid SubscriberId, ChannelReader<ExecutionProgressEvent> Reader) Subscribe(Guid workflowId);

    /// <summary>
    /// Removes a subscriber and completes its channel.
    /// </summary>
    /// <param name="workflowId">The workflow identifier.</param>
    /// <param name="subscriberId">The subscriber identifier returned by <see cref="Subscribe"/>.</param>
    void Unsubscribe(Guid workflowId, Guid subscriberId);
}
