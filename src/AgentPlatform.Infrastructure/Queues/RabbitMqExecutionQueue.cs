using System.Globalization;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace AgentPlatform.Infrastructure.Queues;

/// <summary>
/// RabbitMQ 执行队列（F37 决策 D1=B 后端之一）。
/// · durable 队列 + <c>BasicPublish(persistent)</c> 投递（await 发布完成 = v7 默认 publisher confirm 落盘确认）；
///   消费端 <c>BasicGet(autoAck:false)</c> 拉模式，与 worker 循环模型一致（无需 async consumer 回调）。
///   注：prefetch 仅作用于 push 消费者，BasicGet 拉模式天然一次一条；此处 QoS 为 push 路径预留语义占位。
/// · worker 崩溃时未 ack 消息由 broker 在连接/channel 断开后重投（RabbitMQ 内建 redelivery），
///   重复投递由 F30 租约互斥 + 终态幂等跳过兜底；接管时效 = broker 检测断连（秒级）≤ 租约 TTL（决策 D3=A 一致性说明）。
/// · 超限失败 → 转投 durable 死信队列；死信写入失败回报 false，worker 保留原投递不 ack（不丢任务）。
/// 连接不可用 → 入队显式 <see cref="EnqueueResult.RejectedBackendUnavailable"/>。
/// 单 <see cref="IChannel"/> 的全部 Basic* 操作（含发布/拉取/ack）在同一 <see cref="SemaphoreSlim"/> 下串行
/// （AMQP channel 非线程安全）。<b>deliveryTag 仅在产生它的 channel 有效期内有效</b>：receipt 编码
/// channel epoch，跨 epoch 的 ack 直接跳过（旧 channel 已关 → broker 已重投该消息，重复执行由租约/幂等兜底）。
/// </summary>
internal sealed class RabbitMqExecutionQueue : IExecutionQueue, IAsyncDisposable
{
    private readonly DurableExecutionSettings _settings;
    private readonly ILogger<RabbitMqExecutionQueue> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _url;
    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>channel 世代号：每次重建 channel 递增，用于界定 deliveryTag 的有效性。</summary>
    private long _epoch;

    public RabbitMqExecutionQueue(
        IOptions<DurableExecutionSettings> settings,
        IConfiguration configuration,
        ILogger<RabbitMqExecutionQueue> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        // 配置链：DurableExecution:RabbitMqUrl → ConnectionStrings:RabbitMQ → amqp://localhost。
        // 空串/空白视同未配置（与 RabbitMqUrl 文档语义「空则回退」一致；空串进 new Uri 会永久失败）。
        _url = FirstNonEmpty(settings.Value.RabbitMqUrl, configuration["ConnectionStrings:RabbitMQ"]) ?? "amqp://localhost";
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <inheritdoc />
    public string Backend => "RabbitMQ";

    /// <inheritdoc />
    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            return await WithChannelAsync(ct, static _ => Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ queue probe failed — queue operations will report RejectedBackendUnavailable");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(job);
        try
        {
            var published = await WithChannelAsync(ct, channel => channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _settings.RabbitQueueName,
                mandatory: false,
                basicProperties: new BasicProperties { Persistent = true },
                body: payload,
                cancellationToken: ct).AsTask());

            // WithChannelAsync=false 表示建连失败 → 后端不可用；发布 await 完成 = v7 publisher confirm 成功。
            return published ? EnqueueResult.Enqueued : EnqueueResult.RejectedBackendUnavailable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to enqueue execution job {JobId} to RabbitMQ", job.JobId);
            return EnqueueResult.RejectedBackendUnavailable;
        }
    }

    /// <inheritdoc />
    public async Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default)
    {
        try
        {
            return await WithChannelAsync<QueueDelivery>(ct, async channel =>
            {
                var result = await channel.BasicGetAsync(_settings.RabbitQueueName, autoAck: false, ct);
                if (result is null)
                {
                    return null;
                }

                ExecutionJob? job;
                try
                {
                    job = JsonSerializer.Deserialize<ExecutionJob>(result.Body.Span);
                }
                catch (JsonException ex)
                {
                    job = null;
                    _logger.LogError(ex, "RabbitMQ delivery {Tag} has unparsable payload — will drop", result.DeliveryTag);
                }

                if (job is null)
                {
                    // 无法反序列化的毒消息：ack 丢弃并记 error（不阻塞队列）。
                    await channel.BasicAckAsync(result.DeliveryTag, multiple: false, ct);
                    return null;
                }

                // receipt = channel epoch + delivery tag（锁内读 epoch）：ack 时校验，杜绝跨 channel 误 ack。
                var epoch = Volatile.Read(ref _epoch);
                return new QueueDelivery(job, $"{epoch.ToString(CultureInfo.InvariantCulture)}:{result.DeliveryTag.ToString(CultureInfo.InvariantCulture)}");
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RabbitMQ read failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string receipt, CancellationToken ct = default)
    {
        var separatorIndex = receipt.IndexOf(':');
        if (separatorIndex <= 0
            || !long.TryParse(receipt[..separatorIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var receiptEpoch)
            || !ulong.TryParse(receipt[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var deliveryTag))
        {
            _logger.LogWarning("RabbitMQ ack skipped — unparsable receipt {Receipt}", receipt);
            return;
        }

        if (receiptEpoch != Volatile.Read(ref _epoch))
        {
            // 审查修复（deliveryTag 跨 channel 有效性）：读后发生过重连，旧 tag 在新 channel 上无意义，
            // 盲 ack 会报 INVALID_DELIVERY 甚至误确认他人消息。旧 channel 断开时 broker 已重投该消息，
            // 此处跳过 ack，由重投 + 租约互斥/终态幂等兜底。
            _logger.LogWarning(
                "RabbitMQ ack skipped — delivery tag belongs to a previous channel epoch ({ReceiptEpoch} vs current {CurrentEpoch}); broker redelivery will resurface it",
                receiptEpoch, _epoch);
            return;
        }

        await WithChannelAsync(ct, channel => channel.BasicAckAsync(deliveryTag, multiple: false, ct).AsTask());
    }

    /// <inheritdoc />
    public async Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default)
    {
        try
        {
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new { job, reason, deadLetteredAt = DateTime.UtcNow });
            var published = await WithChannelAsync(ct, channel => channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _settings.RabbitDeadLetterQueueName,
                mandatory: false,
                basicProperties: new BasicProperties { Persistent = true },
                body: envelope,
                cancellationToken: ct).AsTask());

            if (!published)
            {
                _logger.LogError("RabbitMQ unavailable — cannot dead-letter job {JobId}: {Reason}", job.JobId, reason);
                return false;
            }

            _logger.LogWarning("Execution job {JobId} moved to RabbitMQ dead-letter queue {Queue}: {Reason}",
                job.JobId, _settings.RabbitDeadLetterQueueName, reason);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to dead-letter job {JobId}: {Reason}", job.JobId, reason);
            return false;
        }
    }

    /// <summary>
    /// 在 <see cref="_gate"/> 串行保护下执行「确保 channel 可用 → 操作」全流程。
    /// 审查修复：原实现仅在重建 channel 时持锁，BasicPublish/BasicGet/BasicAck 裸用共享 channel 并发交叠；
    /// AMQP channel 帧交错可致协议错误/RPC 响应错配，现全部 I/O 收进同一锁（拉模式低频，代价可接受）。
    /// 返回 false = 建连/重建失败（调用方按后端不可用处理）。
    /// </summary>
    private async Task<T?> WithChannelAsync<T>(CancellationToken ct, Func<IChannel, Task<T?>> op)
        where T : class
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelCoreAsync(ct);
            if (channel is null)
            {
                return null;
            }

            return await op(channel);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>布尔版：op 成功执行返回 true；建连/重建失败返回 false（调用方按后端不可用处理）。调用方已处理返回值。</summary>
    private async Task<bool> WithChannelAsync(CancellationToken ct, Func<IChannel, Task> op)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await EnsureChannelCoreAsync(ct);
            if (channel is null)
            {
                return false;
            }

            await op(channel);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 建连/重建 channel（必须在 <see cref="_gate"/> 内调用）：
    /// 旧连接先异步释放（资源对称，不泄漏 socket）；每次重建递增 <see cref="_epoch"/> 使旧 deliveryTag 失效；
    /// 队列声明幂等（durable），死信队列同建。
    /// </summary>
    private async Task<IChannel?> EnsureChannelCoreAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        // 先释放旧句柄（断开/关闭状态下为近 no-op，但保持 acquire/release 对称）。
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(_url) };
            _connection = await factory.CreateConnectionAsync("ap-execution-queue", ct);
            var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

            await channel.QueueDeclareAsync(
                queue: _settings.RabbitQueueName, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: ct);
            await channel.QueueDeclareAsync(
                queue: _settings.RabbitDeadLetterQueueName, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: ct);

            _channel = channel;
            // 审查修复：新 channel 的 deliveryTag 从 1 重新计数——世代号递增，
            // 旧世代 receipt 一律拒 ack，防跨 channel 误确认他人消息。
            Interlocked.Increment(ref _epoch);
            return channel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RabbitMQ channel setup failed ({Url})", _url);
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
