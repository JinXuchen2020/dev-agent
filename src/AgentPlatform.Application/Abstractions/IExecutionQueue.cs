namespace AgentPlatform.Application.Abstractions;

/// <summary>
/// F37 队列化执行：一次待执行的 workflow run 载荷。
/// 必须自带完整执行上下文（租户/工作空间/触发信封）——worker 在新 DI scope 中
/// 无法从 HTTP 请求恢复上下文，只能靠载荷复现（决策 D2=B：run 端点透明入队）。
/// </summary>
/// <param name="JobId">作业标识（幂等键；重试沿用同一 Id，Attempt 递增）。</param>
/// <param name="WorkflowId">要执行的工作流。</param>
/// <param name="TenantId">租户上下文（worker 侧设 ITenantContext.OverrideTenantId）。</param>
/// <param name="WorkspaceId">工作空间上下文（worker 侧设 IWorkspaceContext.OverrideWorkspaceId，F35）。</param>
/// <param name="Preset">编排预设（序列化数值，对齐既有 API 枚举 int 约定）。</param>
/// <param name="TriggerType">触发来源（null = 人工运行）。</param>
/// <param name="PayloadJson">Webhook 触发载荷（仅 TriggerType=Webhook 时有值）。</param>
/// <param name="RequestingUserId">发起用户（审计归属，可空）。</param>
/// <param name="Attempt">第几次投递（1 起）；worker 失败重试时 +1。</param>
/// <param name="EnqueuedAt">首次入队时间（UTC）。</param>
public sealed record ExecutionJob(
    Guid JobId,
    Guid WorkflowId,
    Guid TenantId,
    Guid WorkspaceId,
    int Preset,
    int? TriggerType = null,
    string? PayloadJson = null,
    Guid? RequestingUserId = null,
    int Attempt = 1,
    DateTime? EnqueuedAt = null)
{
    /// <summary>副本（重试投递：Attempt+1）。</summary>
    public ExecutionJob NextAttempt() => this with { Attempt = Attempt + 1 };
}

/// <summary>worker 取出的一次投递：作业 + 后端相关的 ack 凭据。</summary>
/// <param name="Job">作业载荷。</param>
/// <param name="Receipt">ack 凭据（Redis=stream entry id；RabbitMQ=delivery tag；InMemory=空占位）。</param>
public sealed record QueueDelivery(ExecutionJob Job, string Receipt);

/// <summary><see cref="IExecutionQueue.EnqueueAsync"/> 的结果。</summary>
public enum EnqueueResult
{
    /// <summary>已成功入队。</summary>
    Enqueued,

    /// <summary>队列已满（有界背压）——调用方须显式失败，绝不静默丢任务。</summary>
    RejectedQueueFull,

    /// <summary>后端不可用（Redis/RabbitMQ 连接失败）——调用方显式失败或按配置降级。</summary>
    RejectedBackendUnavailable,
}

/// <summary>
/// 执行队列抽象（F37）。三后端：InMemory（默认）/ RedisStream / RabbitMQ，
/// 按 <c>DurableExecution:QueueBackend</c> 条件注册。语义 = 至少一次投递：
/// 重复消费由 F30 RunningExecution 租约互斥兜底（worker 拿不到租约即 ack 跳过）。
/// </summary>
public interface IExecutionQueue
{
    /// <summary>后端名（日志/诊断）："InMemory" | "RedisStream" | "RabbitMQ"。</summary>
    string Backend { get; }

    /// <summary>
    /// 当前待消费作业数（F39 观测用；Prometheus 侧 <c>execution_queue_depth{backend=...}</c>）。
    /// 必须是同步廉价读且绝不抛出（不可用返回 0），以免拖垮 scrape：
    /// InMemory = 通道内待消费数（精确，不含已取出在飞）；RedisStream = Stream 长度
    /// （ack 即 XDEL，故 = 未投递 + 在飞，真实积压）；
    /// RabbitMQ = 后台周期刷新的近似值（默认 ≤5s 陈旧窗口，其管理调用无同步廉价形式）。
    /// </summary>
    long QueueDepth { get; }

    /// <summary>可用性探测（启动一次；失败即告警，不影响注册——运行期 Enqueue 仍会显式报不可用）。</summary>
    Task<bool> ProbeAsync(CancellationToken ct = default);

    /// <summary>投递一个执行作业。</summary>
    Task<EnqueueResult> EnqueueAsync(ExecutionJob job, CancellationToken ct = default);

    /// <summary>worker 侧取一条投递（后端各自的阻塞窗口由实现决定，无消息返回 null）。</summary>
    Task<QueueDelivery?> TryReadAsync(CancellationToken ct = default);

    /// <summary>确认投递完成（ack）。</summary>
    Task CompleteAsync(string receipt, CancellationToken ct = default);

    /// <summary>
    /// 超限失败投递进 dead-letter 通道（毒消息不丢、不重试风暴）。
    /// 返回 true = 已确认落存死信通道，worker 方可 ack 原投递；
    /// 返回 false = 死信写入失败（后端不可用等）——worker 必须保留原投递未 ack，交由后端重投语义兜底，
    /// 绝不允许「死信失败但原投递已 ack」的任务彻底丢失路径。
    /// </summary>
    Task<bool> DeadLetterAsync(ExecutionJob job, string reason, CancellationToken ct = default);
}
