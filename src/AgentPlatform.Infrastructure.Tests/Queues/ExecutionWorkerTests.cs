using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.ExecuteQueuedWorkflow;
using AgentPlatform.Infrastructure.Queues;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Queues;

/// <summary>
/// F37 ExecutionWorker 语义测试（脚本化假队列驱动，无需真实中间件）：
/// · Executed/Duplicate → ack 原投递，不重投。
/// · Failed 且 Attempt &lt; MaxAttempts → 以 Attempt+1 重投 + ack 原投递。
/// · Failed 且 Attempt == MaxAttempts → dead-letter + ack（毒消息不重试风暴、不静默丢）。
/// </summary>
public sealed class ExecutionWorkerTests
{
    /// <summary>脚本化队列：按序吐出预置投递，并记录 ack/重投/dead-letter 调用。</summary>
    private sealed class ScriptedQueue : IExecutionQueue
    {
        private readonly Queue<QueueDelivery> _deliveries = new();

        public string Backend => "Scripted";

        public List<ExecutionJob> ReEnqueued { get; } = [];
        public List<(ExecutionJob Job, string Reason)> DeadLettered { get; } = [];
        public List<string> Acked { get; } = [];
        public EnqueueResult NextEnqueueResult { get; set; } = EnqueueResult.Enqueued;
        public bool DeadLetterSucceeds { get; set; } = true;

        public void Provide(ExecutionJob job) =>
            _deliveries.Enqueue(new QueueDelivery(job, $"receipt-{job.Attempt}-{_deliveries.Count}"));

        public Task<bool> ProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default)
        {
            ReEnqueued.Add(job);
            return Task.FromResult(NextEnqueueResult);
        }

        public Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default) =>
            Task.FromResult(_deliveries.Count > 0 ? _deliveries.Dequeue() : null);

        public Task CompleteAsync(string receipt, CancellationToken ct = default)
        {
            Acked.Add(receipt);
            return Task.CompletedTask;
        }

        public Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default)
        {
            DeadLettered.Add((job, reason));
            return Task.FromResult(DeadLetterSucceeds);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var started = DateTime.UtcNow;
        while (!condition() && (DateTime.UtcNow - started).TotalMilliseconds < timeoutMs)
        {
            await Task.Delay(25);
        }
    }

    private static ExecutionJob Job(int attempt = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Attempt: attempt);

    [Fact]
    public async Task Executed_Is_Acked_And_Not_Retried()
    {
        var job = Job();
        var queue = new ScriptedQueue();
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Executed));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.Acked.Count == 1);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(queue.Acked);
        Assert.Empty(queue.ReEnqueued);
        Assert.Empty(queue.DeadLettered);
        worker.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Duplicate_Is_Acked_And_Not_Retried()
    {
        var job = Job();
        ScriptedQueue? captured = null;

        var queue = new ScriptedQueue();
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Duplicate));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.Acked.Count == 1);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        captured = queue;
        Assert.Single(captured.Acked);
        Assert.Empty(captured.ReEnqueued);
        Assert.Empty(captured.DeadLettered);
        worker.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Failed_Below_MaxAttempts_Is_Reenqueued_With_Incremented_Attempt()
    {
        var job = Job(attempt: 1);
        var queue = new ScriptedQueue();
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Failed));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.Acked.Count >= 2); // 原投递 ack + 重投后被再次消费 ack
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(queue.ReEnqueued, j => j.Attempt == 2 && j.JobId == job.JobId);
        Assert.Empty(queue.DeadLettered);
        worker.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Failed_At_MaxAttempts_Goes_To_DeadLetter()
    {
        var job = Job(attempt: 3);
        var queue = new ScriptedQueue();
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Failed));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.DeadLettered.Count == 1 && queue.Acked.Count == 1);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(queue.ReEnqueued);
        Assert.Single(queue.DeadLettered);
        Assert.Equal(job.JobId, queue.DeadLettered[0].Job.JobId);
        Assert.Contains("3 attempts", queue.DeadLettered[0].Reason, StringComparison.Ordinal);
        worker.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Failed_At_MaxAttempts_DeadLetterWriteFails_Keeps_Delivery_Unacked()
    {
        // F3 修复守卫：死信落存失败（后端不可用）时绝不 ack 原投递——否则任务彻底丢失。
        var job = Job(attempt: 3);
        var queue = new ScriptedQueue { DeadLetterSucceeds = false };
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Failed));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.DeadLettered.Count == 1);
        await Task.Delay(200); // 给可能的（错误）ack 留时间窗
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(queue.DeadLettered);
        Assert.Empty(queue.Acked); // 关键：未接管成功 → 不 ack，后端重投语义兜底
        worker.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Failed_Retry_EnqueueRejected_Keeps_Delivery_Unacked()
    {
        // F3 修复守卫：重试重投被拒（队列满/后端不可用）时不 ack 原投递。
        var job = Job(attempt: 1);
        var queue = new ScriptedQueue { NextEnqueueResult = EnqueueResult.RejectedQueueFull };
        queue.Provide(job);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ExecuteQueuedWorkflowCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(QueuedRunOutcome.Failed));
        var provider = new ServiceCollection().AddSingleton(mediator).BuildServiceProvider();
        var worker = new ExecutionWorker(
            queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DurableExecutionSettings { QueueEnabled = true, QueueMaxAttempts = 3 }),
            NullLogger<ExecutionWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await WaitUntilAsync(() => queue.ReEnqueued.Count >= 1);
        await Task.Delay(200);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(queue.ReEnqueued, j => j.Attempt == 2);
        Assert.Empty(queue.Acked); // 重投未成功 → 保留原投递
        worker.Dispose();
        await provider.DisposeAsync();
    }
}
