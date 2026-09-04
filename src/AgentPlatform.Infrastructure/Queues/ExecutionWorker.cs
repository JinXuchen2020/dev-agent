using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.ExecuteQueuedWorkflow;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Queues;

/// <summary>
/// 执行队列 worker（F37）：<c>DurableExecution:QueueEnabled=true</c> 时注册的 BackgroundService。
/// 循环 TryRead → 独立 DI scope 内 <see cref="ExecuteQueuedWorkflowCommand"/>（载荷复现租户/工作空间上下文，
/// F30 租约互斥防重复驱动）→ ack。失败重试：Attempt &lt; <c>QueueMaxAttempts</c> 时以 +1 重投，
/// 超限进 dead-letter；<b>仅当重投/死信确认接管成功才 ack 原投递</b>（接管失败 → 不 ack，
/// 由后端重投语义兜底，杜绝死信失败即丢任务）。取消（停机）时不 ack——持久后端（Redis/RabbitMQ）
/// 由其重投语义接管。
/// </summary>
internal sealed class ExecutionWorker : BackgroundService
{
    private readonly IExecutionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DurableExecutionSettings _settings;
    private readonly ILogger<ExecutionWorker> _logger;

    public ExecutionWorker(
        IExecutionQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<DurableExecutionSettings> settings,
        ILogger<ExecutionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 恒注册 + 运行时门控（配置延迟快照修复）：QueueEnabled=false 立即退出，零行为变化。
        if (!_settings.QueueEnabled)
        {
            _logger.LogDebug("Execution worker idle-exiting: DurableExecution:QueueEnabled=false");
            return;
        }

        _logger.LogInformation(
            "Execution worker started (backend={Backend}, maxAttempts={MaxAttempts})",
            _queue.Backend, _settings.QueueMaxAttempts);

        try
        {
            var probeOk = await _queue.ProbeAsync(stoppingToken);
            if (!probeOk)
            {
                // fail-safe：探测失败不抛崩宿主；读循环会持续重试并在日志中显性告警。
                _logger.LogWarning(
                    "Execution queue backend {Backend} unavailable at startup — worker keeps retrying",
                    _queue.Backend);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            QueueDelivery? delivery = null;
            try
            {
                delivery = await _queue.TryReadAsync(stoppingToken);
                if (delivery is null)
                {
                    await Task.Delay(Math.Max(50, _settings.WorkerIdleDelayMilliseconds), stoppingToken);
                    continue;
                }

                var ackDelivery = await ProcessAsync(delivery.Job, stoppingToken);
                // 审查修复（消息可靠性）：仅当失败已被重试投递/死信通道接管时才 ack 原投递；
                // 接管动作本身失败（后端不可用）→ 不 ack，Redis PEL / Rabbit unacked 重投语义兜底，
                // 杜绝「dead-letter 写入失败但原投递已 ack」导致任务彻底丢失。
                if (ackDelivery)
                {
                    await _queue.CompleteAsync(delivery.Receipt, stoppingToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Delivery {Receipt} left un-acked: failure hand-off (retry/dead-letter) did not succeed; backend redelivery will resurface it",
                        delivery.Receipt);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 停机：未 ack 的投递留给后端的可见性/重投语义接管（InMemory 后端为已知限制）。
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution worker iteration failed");
                if (delivery is not null)
                {
                    var handedOff = await HandleFailureAsync(delivery.Job, ex.Message, stoppingToken);
                    if (handedOff)
                    {
                        try
                        {
                            await _queue.CompleteAsync(delivery.Receipt, CancellationToken.None);
                        }
                        catch (Exception ackEx)
                        {
                            _logger.LogWarning(ackEx, "Failed to ack delivery {Receipt} after processing error", delivery.Receipt);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Delivery {Receipt} left un-acked after processing error: retry/dead-letter hand-off failed",
                            delivery.Receipt);
                    }
                }
                else
                {
                    await Task.Delay(1000, CancellationToken.None);
                }
            }
        }

        _logger.LogInformation("Execution worker stopped (backend={Backend})", _queue.Backend);
    }

    private async Task<bool> ProcessAsync(ExecutionJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var outcome = await mediator.Send(new ExecuteQueuedWorkflowCommand(job), ct);
        // 成功/毒消息丢弃/重复投递 → ack；失败且未完成重试或死信接管 → 不 ack（保留重投）。
        return outcome != QueuedRunOutcome.Failed || await HandleFailureAsync(job, "execution returned Failed", ct);
    }

    /// <summary>返回 true = 失败已被重投或死信通道接管（可 ack）；false = 接管失败（须保留原投递待重投）。</summary>
    private async Task<bool> HandleFailureAsync(ExecutionJob job, string reason, CancellationToken ct)
    {
        if (job.Attempt < Math.Max(1, _settings.QueueMaxAttempts))
        {
            var retry = job.NextAttempt();
            var result = await _queue.EnqueueAsync(retry, ct);
            _logger.LogWarning(
                "Execution job {JobId} failed ({Reason}); re-enqueued attempt {Attempt}/{Max} with result {Result}",
                job.JobId, reason, retry.Attempt, _settings.QueueMaxAttempts, result);
            return result == EnqueueResult.Enqueued;
        }

        var deadLettered = await _queue.DeadLetterAsync(job, $"{reason} (after {job.Attempt} attempts)", ct);
        if (!deadLettered)
        {
            _logger.LogError(
                "Execution job {JobId} exceeded max attempts but dead-letter write FAILED ({Reason}); original delivery kept un-acked for redelivery",
                job.JobId, reason);
        }

        return deadLettered;
    }
}
