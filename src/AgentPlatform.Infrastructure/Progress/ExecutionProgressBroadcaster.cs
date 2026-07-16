using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentPlatform.Infrastructure.Progress;

/// <summary>
/// Singleton broadcaster that manages per-workflow event channels for SSE streaming.
/// Each subscriber creates a bounded channel; events fan out to all subscribers of a workflow.
/// Channels are removed when a subscriber cancels or disconnects.
/// </summary>
internal sealed class ExecutionProgressBroadcaster : IExecutionProgressBroadcaster, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ExecutionProgressEvent>>> _channels = new();
    private readonly ILogger<ExecutionProgressBroadcaster> _logger;

    public ExecutionProgressBroadcaster(ILogger<ExecutionProgressBroadcaster> logger)
    {
        _logger = logger;
    }

    public ValueTask PublishAsync(
        Guid workflowId, ExecutionProgressEvent @event, CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(workflowId, out var subscribers))
            return ValueTask.CompletedTask;

        var deadChannelIds = new List<Guid>();

        foreach (var kvp in subscribers)
        {
            var channel = kvp.Value;
            try
            {
                // Try to write without blocking; drop if full (subscriber too slow)
                if (!channel.Writer.TryWrite(@event))
                {
                    // Channel is full — DropWrite mode silently drops; check completion
                    if (channel.Reader.Completion.IsCompleted)
                        deadChannelIds.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write to progress channel for workflow {WorkflowId}", workflowId);
                deadChannelIds.Add(kvp.Key);
            }
        }

        // Clean up dead channels by specific ID
        foreach (var id in deadChannelIds)
        {
            if (subscribers.TryRemove(id, out var deadChannel))
            {
                deadChannel.Writer.TryComplete();
            }
        }

        return ValueTask.CompletedTask;
    }

    public (Guid SubscriberId, ChannelReader<ExecutionProgressEvent> Reader) Subscribe(Guid workflowId)
    {
        var channel = Channel.CreateBounded<ExecutionProgressEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

        var subscriberId = Guid.NewGuid();
        _channels.AddOrUpdate(
            workflowId,
            _ => new ConcurrentDictionary<Guid, Channel<ExecutionProgressEvent>> { [subscriberId] = channel },
            (_, dict) =>
            {
                dict[subscriberId] = channel;
                return dict;
            });

        _logger.LogDebug("Subscriber added for workflow {WorkflowId} (subscriberId: {SubscriberId})", workflowId, subscriberId);
        return (subscriberId, channel.Reader);
    }

    public void Unsubscribe(Guid workflowId, Guid subscriberId)
    {
        if (_channels.TryGetValue(workflowId, out var subscribers))
        {
            if (subscribers.TryRemove(subscriberId, out var channel))
            {
                channel.Writer.TryComplete();
                _logger.LogDebug("Subscriber {SubscriberId} removed for workflow {WorkflowId}", subscriberId, workflowId);
            }
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _channels)
        {
            foreach (var channel in kvp.Value.Values)
            {
                channel.Writer.TryComplete();
            }
        }
        _channels.Clear();
    }
}
