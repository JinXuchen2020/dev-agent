using AgentPlatform.Application.Abstractions;
using AgentPlatform.Infrastructure.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests.Queues;

/// <summary>
/// F37 队列后端测试：
/// · InProcessExecutionQueue：FIFO、有界背压显式拒投（绝不静默丢任务）。
/// · RedisStream / RabbitMQ：SkippableFact 门控（broker 不可达即跳过，CI ubuntu 提供真实中间件），
///   验证入队→消费→ack 的投递闭环与探测语义（决策 D1=B 三后端）。
/// </summary>
public class ExecutionQueueTests
{
    private static DurableExecutionSettings Settings(
        string backend = "InMemory", int capacity = 4, int leaseTtlMinutes = 5) => new()
        {
            QueueEnabled = true,
            QueueBackend = backend,
            QueueCapacity = capacity,
            LeaseTtlMinutes = leaseTtlMinutes,
        };

    private static ExecutionJob Job(Guid? workflowId = null) =>
        new(Guid.NewGuid(), workflowId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Attempt: 1);

    [Fact]
    public async Task InProcessQueue_Fifo_And_Job_RoundTrip()
    {
        using var queue = new InProcessExecutionQueue(
            Options.Create(Settings()), NullLogger<InProcessExecutionQueue>.Instance);
        var first = Job();
        var second = Job();

        Assert.Equal(EnqueueResult.Enqueued, await queue.EnqueueAsync(first));
        Assert.Equal(EnqueueResult.Enqueued, await queue.EnqueueAsync(second));

        var read1 = await queue.TryReadAsync();
        var read2 = await queue.TryReadAsync();

        Assert.NotNull(read1);
        Assert.Equal(first.JobId, read1!.Job.JobId);
        Assert.Equal(second.JobId, (await Task.FromResult(read2))!.Job.JobId);
        // ack 语义为占位（进程内消费即出队），不得抛。
        await queue.CompleteAsync(read1.Receipt);
    }

    [Fact]
    public async Task InProcessQueue_Returns_RejectedQueueFull_When_Bounded()
    {
        using var queue = new InProcessExecutionQueue(
            Options.Create(Settings(capacity: 2)), NullLogger<InProcessExecutionQueue>.Instance);

        // 前两次填满有界通道。
        Assert.Equal(EnqueueResult.Enqueued, await queue.EnqueueAsync(Job()));
        Assert.Equal(EnqueueResult.Enqueued, await queue.EnqueueAsync(Job()));

        // 第三次必须显式拒绝（有界背压），而非静默丢弃或无限增长。
        Assert.Equal(EnqueueResult.RejectedQueueFull, await queue.EnqueueAsync(Job()));
    }

    [Fact]
    public async Task InProcessQueue_Empty_Read_Returns_Null_Within_Window()
    {
        using var queue = new InProcessExecutionQueue(
            Options.Create(Settings()), NullLogger<InProcessExecutionQueue>.Instance);

        var delivery = await queue.TryReadAsync(CancellationToken.None);

        Assert.Null(delivery);
    }

    /// <summary>
    /// 与生产解析链一致的测试侧凭据注入：CI 经 <c>ConnectionStrings__RabbitMQ</c> 提供非 guest
    /// 用户（guest 默认仅允许 loopback 登录，GH Actions 网桥流量会被拒 → 门控恒跳过）。
    /// 本地开发不设该变量 = 回退 amqp://localhost（guest 本机 loopback 可用）。
    /// </summary>
    private static DurableExecutionSettings BrokerSettings(string backend, int capacity = 4)
    {
        var settings = Settings(backend, capacity);
        if (backend == "RabbitMQ"
            && Environment.GetEnvironmentVariable("ConnectionStrings__RabbitMQ") is { Length: > 0 } url)
        {
            settings.RabbitMqUrl = url;
        }

        return settings;
    }

    [SkippableFact]
    public async Task RedisStreamQueue_Enqueue_Read_Ack_RoundTrip()
    {
        await using var queue = new RedisStreamExecutionQueue(
            Options.Create(BrokerSettings("RedisStream")), new Microsoft.Extensions.Configuration.ConfigurationManager(),
            NullLogger<RedisStreamExecutionQueue>.Instance);
        var job = Job();

        // 门控 = 真实可用性（端口可达但非 Redis 协议 / 版本不支持 Stream / 需鉴权均视为不可用）。
        Skip.IfNot(
            await queue.ProbeAsync(CancellationToken.None)
                && await queue.EnqueueAsync(job, CancellationToken.None) is EnqueueResult.Enqueued,
            "local Redis not usable for Stream operations (skipped)");

        var delivery = await queue.TryReadAsync(CancellationToken.None);
        Assert.NotNull(delivery);
        Assert.Equal(job.JobId, delivery!.Job.JobId);
        Assert.Equal(job.WorkspaceId, delivery.Job.WorkspaceId); // 载荷完整复现上下文

        await queue.CompleteAsync(delivery.Receipt, CancellationToken.None);
        Assert.Null(await queue.TryReadAsync(CancellationToken.None));

        // 验收 4（broker 级）：死信真实落盘——DeadLetterAsync 仅在 XADD 成功时返回 true。
        Assert.True(await queue.DeadLetterAsync(job, "roundtrip-test", CancellationToken.None));
    }

    [SkippableFact]
    public async Task RabbitMqQueue_Enqueue_Read_Ack_RoundTrip()
    {
        await using var queue = new RabbitMqExecutionQueue(
            Options.Create(BrokerSettings("RabbitMQ")), new Microsoft.Extensions.Configuration.ConfigurationManager(),
            NullLogger<RabbitMqExecutionQueue>.Instance);
        var job = Job();

        // 门控 = 真实可用性（broker 不可达 / 鉴权失败均跳过）。
        Skip.IfNot(
            await queue.ProbeAsync(CancellationToken.None)
                && await queue.EnqueueAsync(job, CancellationToken.None) is EnqueueResult.Enqueued,
            "local RabbitMQ not usable (skipped)");

        var delivery = await queue.TryReadAsync(CancellationToken.None);
        Assert.NotNull(delivery);
        Assert.Equal(job.JobId, delivery!.Job.JobId);

        await queue.CompleteAsync(delivery.Receipt, CancellationToken.None);

        // 验收 4（broker 级）：死信真实落盘——DeadLetterAsync 仅在发布确认成功时返回 true。
        Assert.True(await queue.DeadLetterAsync(job, "roundtrip-test", CancellationToken.None));
    }


}
