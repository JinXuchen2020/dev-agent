using System.Threading.Channels;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Progress;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Progress;

public sealed class ExecutionProgressBroadcasterTests
{
    private readonly ILogger<ExecutionProgressBroadcaster> _logger =
        Substitute.For<ILogger<ExecutionProgressBroadcaster>>();

    private static ExecutionProgressEvent CreateTestEvent(Guid workflowId) =>
        new(
            Type: "test_event",
            WorkflowId: workflowId,
            ExecutionLogId: null,
            StepName: null,
            StepOrder: null,
            Status: "running",
            Result: null,
            ErrorDetail: null,
            Timestamp: DateTime.UtcNow);

    [Fact]
    public async Task PublishAsync_ThenReaderReceives_Event()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(_logger);
        var workflowId = Guid.NewGuid();
        var (_, reader) = broadcaster.Subscribe(workflowId);
        var expectedEvent = CreateTestEvent(workflowId);

        // Act
        await broadcaster.PublishAsync(workflowId, expectedEvent);

        // Assert
        var completed = reader.TryRead(out var actualEvent);
        Assert.True(completed);
        Assert.Same(expectedEvent, actualEvent);
    }

    [Fact]
    public async Task PublishAsync_DoesNotThrow_ForUnknownWorkflow()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(_logger);
        var workflowId = Guid.NewGuid();

        // Act & Assert
        var exception = await Record.ExceptionAsync(
            () => broadcaster.PublishAsync(workflowId, CreateTestEvent(workflowId)).AsTask());

        Assert.Null(exception);
    }

    [Fact]
    public void Subscribe_ReturnsNonEmptySubscriberId()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(_logger);
        var workflowId = Guid.NewGuid();

        // Act
        var (subscriberId, _) = broadcaster.Subscribe(workflowId);

        // Assert
        Assert.NotEqual(Guid.Empty, subscriberId);
    }

    [Fact]
    public void Dispose_CompletesAllChannels()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(_logger);
        var workflowId1 = Guid.NewGuid();
        var workflowId2 = Guid.NewGuid();
        var (_, reader1) = broadcaster.Subscribe(workflowId1);
        var (_, reader2) = broadcaster.Subscribe(workflowId2);

        // Act
        broadcaster.Dispose();

        // Assert: both readers should be completed (no more events)
        Assert.True(reader1.Completion.IsCompleted);
        Assert.True(reader2.Completion.IsCompleted);
    }

    [Fact]
    public async Task MultipleWorkflows_AreIndependent()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(_logger);
        var wf1 = Guid.NewGuid();
        var wf2 = Guid.NewGuid();
        var (_, reader1) = broadcaster.Subscribe(wf1);
        var (_, reader2) = broadcaster.Subscribe(wf2);

        var event1 = CreateTestEvent(wf1);
        var event2 = CreateTestEvent(wf2);

        // Act
        await broadcaster.PublishAsync(wf1, event1);

        // Assert: reader1 gets the event, reader2 does not
        Assert.True(reader1.TryRead(out var received1));
        Assert.Same(event1, received1);

        Assert.False(reader2.TryRead(out _));
    }
}
