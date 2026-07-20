using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Progress;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Progress;

/// <summary>
/// Regression tests for the SSE subscription lifecycle.
/// These lock the Phase-3 P1 fix: a subscriber that disconnects or completes
/// must be removed from the broadcaster's internal dictionary, otherwise the
/// Singleton broadcaster leaks one Channel per connection for the process lifetime.
/// </summary>
public sealed class ExecutionProgressBroadcasterTests
{
    // Reads the private ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<...>>>
    // to assert subscription entries are actually removed (the leak invariant).
    private static int GetSubscriberCount(ExecutionProgressBroadcaster broadcaster, Guid workflowId)
    {
        var field = typeof(ExecutionProgressBroadcaster)
            .GetField("_channels", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ExecutionProgressEvent>>>)
            field.GetValue(broadcaster)!;
        return dict.TryGetValue(workflowId, out var inner) ? inner.Count : 0;
    }

    [Fact]
    public void Subscribe_ThenUnsubscribe_RemovesSubscriberChannel()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(Substitute.For<ILogger<ExecutionProgressBroadcaster>>());
        var workflowId = Guid.NewGuid();

        // Act
        var (subscriberId, _) = broadcaster.Subscribe(workflowId);

        // Assert: subscription registered
        Assert.Equal(1, GetSubscriberCount(broadcaster, workflowId));

        broadcaster.Unsubscribe(workflowId, subscriberId);

        // Assert: channel removed -> no leak
        Assert.Equal(0, GetSubscriberCount(broadcaster, workflowId));
    }

    [Fact]
    public void Unsubscribe_IsIdempotent_ForRemovedOrUnknownSubscriber()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(Substitute.For<ILogger<ExecutionProgressBroadcaster>>());
        var workflowId = Guid.NewGuid();
        var (subscriberId, _) = broadcaster.Subscribe(workflowId);

        // Act & Assert: double unsubscribe must not throw and must remain clean
        broadcaster.Unsubscribe(workflowId, subscriberId);
        broadcaster.Unsubscribe(workflowId, subscriberId);

        Assert.Equal(0, GetSubscriberCount(broadcaster, workflowId));
    }

    [Fact]
    public void MultipleSubscribers_AreTrackedIndependently()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(Substitute.For<ILogger<ExecutionProgressBroadcaster>>());
        var workflowId = Guid.NewGuid();
        var (s1, _) = broadcaster.Subscribe(workflowId);
        var (s2, _) = broadcaster.Subscribe(workflowId);

        Assert.Equal(2, GetSubscriberCount(broadcaster, workflowId));

        // Act: only one disconnects
        broadcaster.Unsubscribe(workflowId, s1);

        // Assert: the other subscriber's channel survives
        Assert.Equal(1, GetSubscriberCount(broadcaster, workflowId));
    }
}
