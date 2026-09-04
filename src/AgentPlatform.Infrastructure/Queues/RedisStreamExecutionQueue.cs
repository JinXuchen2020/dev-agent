using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AgentPlatform.Infrastructure.Queues;

/// <summary>
/// Redis Stream 执行队列（F37 决策 D1=B 后端之一）。
/// · 投递 = XADD（MAXLEN ~ 有界修剪）；消费 = XREADGROUP（消费组 <c>ap-workers</c>，Block 2s，count 1）。
/// · 接管 = XAUTOCLAIM：pending 空闲超过 <c>LeaseTtlMinutes</c>（决策 D3=A，与 F30 租约窗口一致）的
///   未 ack 消息被存活 worker 认领重放；重复投递由 F30 <c>TryAcquireLease</c> 互斥兜底。
/// · 完成 = XACK + XDEL（F39：删除已 ack 条目，保证 StreamLength 即真实积压）；超限 = 转入 dead-letter Stream。
/// · 连接失败 → 显式 <see cref="EnqueueResult.RejectedBackendUnavailable"/>（不静默丢任务）。
/// 自建 <see cref="ConnectionMultiplexer"/>（AbortOnConnectFail=false），不依赖 Cache:Provider 配置。
/// </summary>
internal sealed class RedisStreamExecutionQueue : IExecutionQueue, IAsyncDisposable
{
    private const string GroupName = "ap-workers";

    /// <summary>XADD MAXLEN ~ 有界修剪上限（毒积压护栏，非按部署可调项）。</summary>
    private const int StreamTrimMaxLength = 100_000;

    private readonly DurableExecutionSettings _settings;
    private readonly ILogger<RedisStreamExecutionQueue> _logger;
    private readonly string _consumerName;
    private readonly SemaphoreSlim _groupInitLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private volatile ConnectionMultiplexer? _connection;
    private bool _groupEnsured;

    public RedisStreamExecutionQueue(
        IOptions<DurableExecutionSettings> settings,
        IConfiguration configuration,
        ILogger<RedisStreamExecutionQueue> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}"[..Math.Min(64, Environment.MachineName.Length + 30)];
        RedisConnectionString = configuration["ConnectionStrings:Redis"]
            ?? configuration["Redis:ConnectionString"]
            ?? "localhost:6379";
        QueueDepthGauge.Register(this);
    }

    /// <summary>Redis 连接串（复用既有配置键，与 Cache 路径同解析链）。</summary>
    public string RedisConnectionString { get; }

    /// <inheritdoc />
    public string Backend => "RedisStream";

    /// <inheritdoc />
    // 只读已建立的连接（绝不在 scrape 路径触发建连/阻塞）；未连接或异常一律返回 0。
    // Stream 长度为 Redis O(1) 命令；ack 时同步 XDEL（见 CompleteAsync），故 XLEN =
    // 未投递 + PEL 在飞条目 = 真实积压（不含已完成的历史条目）。
    public long QueueDepth
    {
        get
        {
            var connection = _connection;
            if (connection is not { IsConnected: true })
            {
                return 0;
            }

            try
            {
                return connection.GetDatabase().StreamLength(_settings.RedisStreamKey);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis queue depth read failed");
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            var mux = await GetConnectionAsync(ct);
            if (mux is null)
            {
                return false;
            }

            await mux.GetDatabase().PingAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis queue probe failed — queue operations will report RejectedBackendUnavailable");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default)
    {
        try
        {
            var mux = await GetConnectionAsync(ct)
                ?? throw new ConnectionFailureException("Redis connection unavailable");

            await mux.GetDatabase().StreamAddAsync(
                _settings.RedisStreamKey,
                new NameValueEntry[] { new("job", JsonSerializer.Serialize(job)) },
                maxLength: StreamTrimMaxLength,
                useApproximateMaxLength: true);
            return EnqueueResult.Enqueued;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to enqueue execution job {JobId} to Redis stream", job.JobId);
            return EnqueueResult.RejectedBackendUnavailable;
        }
    }

    /// <inheritdoc />
    public async Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default)
    {
        try
        {
            var mux = await GetConnectionAsync(ct);
            if (mux is null)
            {
                return null;
            }

            var db = mux.GetDatabase();
            await EnsureConsumerGroupAsync(db, ct);

            // 先认领空闲超限的 pending（worker 崩溃接管，D3=A：idle 阈值 = 租约 TTL）
            var claimed = await TryClaimIdleAsync(db, ct);
            if (claimed is not null)
            {
                return claimed;
            }

            var entries = await db.StreamReadGroupAsync(
                _settings.RedisStreamKey,
                GroupName,
                _consumerName,
                count: 1);

            return entries is { Length: > 0 } ? ToDelivery(entries[0]) : null;
        }
        catch (RedisException ex) when (ex.Message.Contains("NOGROUP", StringComparison.Ordinal))
        {
            // 消费组丢失（stream 被删/重建）：重置确保标记，下一轮重新创建消费组（自愈），本轮回空。
            _groupEnsured = false;
            _logger.LogWarning(ex, "Redis consumer group missing — will recreate on next read");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Redis stream read failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string receipt, CancellationToken ct = default)
    {
        var mux = await GetConnectionAsync(ct);
        if (mux is null)
        {
            return;
        }

        var db = mux.GetDatabase();
        await db.StreamAcknowledgeAsync(_settings.RedisStreamKey, GroupName, [receipt]);

        // F39 修复（积压语义名不副实）：XACK 不会从 Stream 删除条目，XLEN 只随 MAXLEN 修剪——
        // 若不清理，QueueDepth 会随历史流量单调增长（全部消费完仍显示积压），QueueBacklogHigh 假告警。
        // 本 Stream 仅一个消费组（ap-workers），ack 后 XDEL 安全；先 ack 后删，删除失败只会虚增积压，绝不丢任务。
        try
        {
            await db.StreamDeleteAsync(_settings.RedisStreamKey, [receipt]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Redis XDEL after ack failed for {Receipt} (backlog may overcount)", receipt);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default)
    {
        try
        {
            var mux = await GetConnectionAsync(ct);
            if (mux is null)
            {
                _logger.LogError("Redis unavailable — cannot dead-letter job {JobId}: {Reason}", job.JobId, reason);
                return false;
            }

            await mux.GetDatabase().StreamAddAsync(
                _settings.RedisDeadLetterKey,
                new NameValueEntry[]
                {
                    new("job", JsonSerializer.Serialize(job)),
                    new("reason", reason)
                });
            _logger.LogWarning("Execution job {JobId} moved to dead-letter stream {Key}: {Reason}",
                job.JobId, _settings.RedisDeadLetterKey, reason);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to dead-letter job {JobId}: {Reason}", job.JobId, reason);
            return false;
        }
    }

    private async Task<QueueDelivery?> TryClaimIdleAsync(IDatabase db, CancellationToken ct)
    {
        var idleThreshold = TimeSpan.FromMinutes(Math.Max(1, _settings.LeaseTtlMinutes));
        try
        {
            var result = await db.StreamAutoClaimAsync(
                _settings.RedisStreamKey,
                GroupName,
                _consumerName,
                (long)idleThreshold.TotalMilliseconds,
                (RedisValue)"0",
                1);

            return result.ClaimedEntries.Length > 0 ? ToDelivery(result.ClaimedEntries[0]) : null;
        }
        catch (RedisException ex)
        {
            // 老版本 Redis（< 6.2 不支持 XAUTOCLAIM）等场景：降级为不接管（仅影响崩溃恢复时效，不影响正常消费）。
            _logger.LogDebug(ex, "StreamAutoClaim unavailable; crash takeover disabled on this Redis version");
            return null;
        }
    }

    private static QueueDelivery? ToDelivery(StreamEntry entry)
    {
        var payload = entry.Values.FirstOrDefault(v => v.Name == "job").Value;
        if (payload.IsNullOrEmpty)
        {
            return null;
        }

        var job = JsonSerializer.Deserialize<ExecutionJob>((string)payload!);
        return job is null ? null : new QueueDelivery(job, entry.Id.ToString());
    }

    private async Task EnsureConsumerGroupAsync(IDatabase db, CancellationToken ct)
    {
        if (_groupEnsured)
        {
            return;
        }

        await _groupInitLock.WaitAsync(ct);
        try
        {
            if (_groupEnsured)
            {
                return;
            }

            try
            {
                await db.StreamCreateConsumerGroupAsync(_settings.RedisStreamKey, GroupName, StreamPosition.Beginning);
            }
            catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
            {
                // 组已存在（多实例并发初始化）——预期内。
            }

            _groupEnsured = true;
        }
        finally
        {
            _groupInitLock.Release();
        }
    }

    private async Task<ConnectionMultiplexer?> GetConnectionAsync(CancellationToken ct)
    {
        // 审查修复（资源生命周期）：AbortOnConnectFail=false 的 multiplexer 会自行后台重连，
        // 必须单例复用。原实现在 IsConnected=false 时每 2s 读循环新建一个 multiplexer 且不释放旧的
        // → 连接对象/socket/后台计时器无界泄漏。仅在 null 时建一次；建连失败不缓存、下次重试。
        var existing = _connection;
        if (existing is not null)
        {
            return existing;
        }

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            try
            {
                var options = ConfigurationOptions.Parse(RedisConnectionString);
                options.AbortOnConnectFail = false;
                var mux = await ConnectionMultiplexer.ConnectAsync(options);
                _connection = mux;
                return mux;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Redis connection unavailable ({ConnectionString})", RedisConnectionString);
                return null;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _groupInitLock.Dispose();
        _connectLock.Dispose();
    }

    /// <summary>内部：连接不可用时抛给入队路径映射为 RejectedBackendUnavailable。</summary>
    private sealed class ConnectionFailureException(string message) : Exception(message);
}
