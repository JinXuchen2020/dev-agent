using System.Threading.Channels;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Queues;

/// <summary>
/// 进程内执行队列（F37 默认后端）。<see cref="Channel{T}"/> 有界背压（容量
/// <c>DurableExecution:QueueCapacity</c>，默认 256）：队列满时入队显式返回
/// <see cref="EnqueueResult.RejectedQueueFull"/>，绝不静默丢任务。
/// 单进程内消费无 ack 语义（receipt 为占位）；进程重启丢失未消费作业——生产多实例部署
/// 须切 RedisStream/RabbitMQ 后端（持久 + 断线重投），本实现定位 = 开发/单实例回退。
/// </summary>
internal sealed class InProcessExecutionQueue : IExecutionQueue, IDisposable
{
    private readonly Channel<ExecutionJob> _channel;
    private readonly ILogger<InProcessExecutionQueue> _logger;

    public InProcessExecutionQueue(IOptions<DurableExecutionSettings> settings, ILogger<InProcessExecutionQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<ExecutionJob>(new BoundedChannelOptions(Math.Max(1, settings.Value.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <inheritdoc />
    public string Backend => "InMemory";

    /// <inheritdoc />
    public Task<bool> ProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default)
    {
        // TryWrite 在有界通道未满时即刻成功；满时不等待（显式拒投由调用方处理，绝不静默丢任务）。
        return Task.FromResult(_channel.Writer.TryWrite(job)
            ? EnqueueResult.Enqueued
            : EnqueueResult.RejectedQueueFull);
    }

    /// <inheritdoc />
    public async Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default)
    {
        // 2s 读超时轮询（worker 循环自身还有 idle delay，这里保持短阻塞便于停机响应）。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var job = await _channel.Reader.ReadAsync(timeout.Token);
            return new QueueDelivery(job, job.JobId.ToString("N"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string receipt, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default)
    {
        // 进程内后端无持久 dead-letter：毒消息在此显式丢弃并记录含完整载荷的 error 日志（可追溯、不静默）。
        // Redis/Rabbit 后端有真 dead-letter 通道。
        _logger.LogError(
            "In-memory queue has no persistent dead-letter channel — job dropped after max attempts. job={Job} reason={Reason}",
            System.Text.Json.JsonSerializer.Serialize(job), reason);
        return Task.FromResult(true);
    }
}
