using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.WorkflowTriggers;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Workflows;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Scheduling;

/// <summary>
/// 耐久工作流调度器（F30 Durable Execution）。
/// 双重职责：
/// 1. 定时触发器分发（原有功能）：轮询到期的 Schedule 触发器，经 RunDueScheduledWorkflowsCommand 分发。
/// 2. 崩溃恢复驱动器（新增）：扫描租约过期的 RunningExecution，抢占租约并调用 OrchestrationPrimitive.ResumeFromCheckpointAsync。
/// </summary>
internal sealed class WorkflowScheduler : BackgroundService
{
    // 轮询间隔：30s。调度器与恢复扫描共用同一节拍。
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowScheduler> _logger;

    public WorkflowScheduler(IServiceScopeFactory scopeFactory, ILogger<WorkflowScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(s_pollInterval);
        _logger.LogInformation("WorkflowScheduler (Durable) 已启动，轮询间隔 {Interval}s", s_pollInterval.TotalSeconds);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var orchestrationPrimitive = scope.ServiceProvider.GetRequiredService<OrchestrationPrimitive>();
                var runningExecutionRepository = scope.ServiceProvider.GetRequiredService<IRunningExecutionRepository>();
                var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

                // 1. 定时触发器分发（原有逻辑）
                var dispatched = await mediator.Send(
                    new RunDueScheduledWorkflowsCommand(DateTime.UtcNow), stoppingToken);
                if (dispatched > 0)
                    _logger.LogInformation("WorkflowScheduler 分发 {Count} 个到期工作流", dispatched);

                // 2. 耐久恢复：扫描租约过期的执行（F30）
                await RecoverExpiredExecutionsAsync(orchestrationPrimitive, runningExecutionRepository, tenantProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "WorkflowScheduler 轮询失败（下一轮重试）");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// 扫描并恢复租约过期的执行（崩溃恢复）。
    /// 只有成功抢占租约的调度器实例会执行恢复，保证多实例幂等。
    /// </summary>
    private async Task RecoverExpiredExecutionsAsync(
        OrchestrationPrimitive orchestrationPrimitive,
        IRunningExecutionRepository runningExecutionRepository,
        ITenantProvider tenantProvider,
        CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();
        if (tenantId == Guid.Empty)
            return; // No tenant context (e.g., scheduler running without tenant)

        var expiredExecutions = await runningExecutionRepository.GetExpiredLeasesAsync(tenantId, ct);
        if (expiredExecutions.Count == 0)
            return;

        _logger.LogInformation("WorkflowScheduler 发现 {Count} 个租约过期执行，尝试恢复", expiredExecutions.Count);

        foreach (var exec in expiredExecutions)
        {
            try
            {
                // Try to acquire lease for this scheduler instance
                var instanceId = $"{Environment.MachineName}-{Environment.ProcessId}-scheduler";
                if (!exec.TryAcquireLease(instanceId, TimeSpan.FromMinutes(5)))
                {
                    _logger.LogDebug("无法抢占工作流 {WorkflowId} 租约（可能被其他实例抢占）", exec.WorkflowId);
                    continue;
                }

                runningExecutionRepository.Update(exec);
                // Note: We need to save the lease acquisition before calling ResumeFromCheckpointAsync
                // The OrchestrationPrimitive will handle its own SaveChangesAsync internally

                _logger.LogInformation("WorkflowScheduler 抢占租约成功，开始恢复工作流 {WorkflowId}", exec.WorkflowId);

                // Resume from checkpoint (this will re-acquire lease internally and run to completion)
                await orchestrationPrimitive.ResumeFromCheckpointAsync(exec.WorkflowId, ct);

                _logger.LogInformation("WorkflowScheduler 成功恢复工作流 {WorkflowId}", exec.WorkflowId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复工作流 {WorkflowId} 失败", exec.WorkflowId);
                // Release lease on failure so another instance can try next round
                try
                {
                    exec.ReleaseLease($"{Environment.MachineName}-{Environment.ProcessId}-scheduler");
                    runningExecutionRepository.Update(exec);
                }
                catch { /* ignore */ }
            }
        }
    }
}