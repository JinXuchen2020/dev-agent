using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentPlatform.Api.Controllers;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Progress;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentPlatform.IntegrationTests;

/// <summary>
/// Regression tests for the SSE endpoint's subscriber cleanup.
/// The original Phase-3 P1 was: WorkflowProgressController discarded the
/// subscriberId returned by Subscribe and never called Unsubscribe, so every
/// disconnected/completed SSE connection leaked a Channel in the Singleton
/// broadcaster until process restart. These tests drive StreamProgress through
/// both exit paths (client disconnect + terminal event) and assert the
/// controller's own subscriber channel is removed.
/// </summary>
public sealed class WorkflowProgressControllerCleanupTests
{
    private static int GetSubscriberCount(ExecutionProgressBroadcaster broadcaster, Guid workflowId)
    {
        var field = typeof(ExecutionProgressBroadcaster)
            .GetField("_channels", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = (ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ExecutionProgressEvent>>>)
            field.GetValue(broadcaster)!;
        return dict.TryGetValue(workflowId, out var inner) ? inner.Count : 0;
    }

    [Fact]
    public async Task StreamProgress_OnClientDisconnect_CleansUpSubscriberChannel()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(NullLogger<ExecutionProgressBroadcaster>.Instance);
        var controller = new WorkflowProgressController(broadcaster);
        var workflowId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // client already gone before first read

        // Act: controller subscribes, the cancelled loop throws, finally runs Unsubscribe
        await controller.StreamProgress(workflowId, cts.Token);

        // Assert: the controller's subscriber channel was removed -> no SSE leak
        Assert.Equal(0, GetSubscriberCount(broadcaster, workflowId));
    }

    [Fact]
    public async Task StreamProgress_OnTerminalEvent_CleansUpControllerSubscriber()
    {
        // Arrange
        var broadcaster = new ExecutionProgressBroadcaster(NullLogger<ExecutionProgressBroadcaster>.Instance);
        var controller = new WorkflowProgressController(broadcaster);
        var workflowId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        // Publish a terminal event shortly after the controller subscribes so it reads & breaks
        var publishTask = Task.Run(async () =>
        {
            await Task.Delay(20);
            await broadcaster.PublishAsync(
                workflowId,
                new ExecutionProgressEvent("workflow_completed", workflowId, null, null, null, "completed", null, null, DateTime.UtcNow),
                default);
        });

        // Act: happy path — read a terminal event, break, finally Unsubscribe
        await controller.StreamProgress(workflowId, CancellationToken.None);
        await publishTask;

        // Assert: controller's own subscriber was cleaned up even on the break path
        Assert.Equal(0, GetSubscriberCount(broadcaster, workflowId));
    }
}
