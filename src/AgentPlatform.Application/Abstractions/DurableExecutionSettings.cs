namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// Configuration settings for durable workflow execution (F30).
/// Controls lease TTL, checkpoint batching, and recovery behavior.
/// </summary>
public sealed class DurableExecutionSettings
{
    /// <summary>
    /// Lease time-to-live in minutes. After this period without heartbeat,
    /// the workflow execution is considered stalled and eligible for recovery by another scheduler instance.
    /// Default: 5 minutes.
    /// </summary>
    public int LeaseTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of steps to accumulate before flushing a checkpoint to the database.
    /// Default: 5 steps.
    /// </summary>
    public int CheckpointBatchSize { get; set; } = 5;

    /// <summary>
    /// Maximum age of an unflushed checkpoint in seconds.
    /// Even if batch size is not reached, checkpoint is flushed after this duration.
    /// Default: 30 seconds.
    /// </summary>
    public int CheckpointMaxAgeSeconds { get; set; } = 30;

    // ── F37 队列化执行与水平扩展 ──

    /// <summary>
    /// 队列模式总开关（决策 D2=B）。false（默认）= 既有请求内同步直跑路径零变化；
    /// true = run 端点透明「入队 → 等待终态」、触发器投递队列、ExecutionWorker 消费。
    /// </summary>
    public bool QueueEnabled { get; set; }

    /// <summary>
    /// 队列后端（决策 D1=B 三后端）："InMemory"（默认，进程内 Channel 有界）|
    /// "RedisStream"（消费组 + XAUTOCLAIM 空闲回收）| "RabbitMQ"（durable 队列 + pull + 断线重投）。
    /// </summary>
    public string QueueBackend { get; set; } = "InMemory";

    /// <summary>
    /// run 端点入队后等待终态的秒数上限（默认 110，刻意低于前端 axios 120s 超时）。
    /// 超时返回 202 queued（显式不假成功）。
    /// </summary>
    public int QueueWaitTimeoutSeconds { get; set; } = 110;

    /// <summary>等待轮询间隔（秒）。</summary>
    public int QueuePollIntervalSeconds { get; set; } = 2;

    /// <summary>最大投递次数（含首次）；超限进 dead-letter 通道。默认 3。</summary>
    public int QueueMaxAttempts { get; set; } = 3;

    /// <summary>InMemory 后端有界队列容量（满 → Enqueue 显式 RejectedQueueFull，绝不静默丢）。</summary>
    public int QueueCapacity { get; set; } = 256;

    /// <summary>Redis Stream 键名（消费组名 = "ap-workers"，接管 idle 阈值复用 <see cref="LeaseTtlMinutes"/>，决策 D3=A）。</summary>
    public string RedisStreamKey { get; set; } = "ap:exec-queue";

    /// <summary>Redis 死信 Stream 键名。</summary>
    public string RedisDeadLetterKey { get; set; } = "ap:exec-deadletter";

    /// <summary>RabbitMQ 执行队列名（durable）。</summary>
    public string RabbitQueueName { get; set; } = "ap.execution.queue";

    /// <summary>RabbitMQ 死信队列名（durable）。</summary>
    public string RabbitDeadLetterQueueName { get; set; } = "ap.execution.deadletter";

    /// <summary>RabbitMQ 连接 URI（空则回退 ConnectionStrings:RabbitMQ → amqp://localhost）。</summary>
    public string? RabbitMqUrl { get; set; }

    /// <summary>worker 空闲轮询间隔（毫秒）。</summary>
    public int WorkerIdleDelayMilliseconds { get; set; } = 500;
}