using AgentPlatform.Application.WorkflowTriggers;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Scheduling;

/// <summary>
/// 后台调度器（进程内 BackgroundService，v1）。按固定间隔轮询到期的 Schedule 触发器，
/// 经 <see cref="RunDueScheduledWorkflowsCommand"/> 在分布式锁保护下逐个分发执行。
/// 每次轮询创建独立 DI scope（DbContext 为 scoped），并在 scope 内注入租户上下文。
/// </summary>
internal sealed class WorkflowScheduler : BackgroundService
{
    // v1 轮询间隔：30s。后续可平滑升级 Quartz，接口不变。
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
        _logger.LogInformation("WorkflowScheduler 已启动，轮询间隔 {Interval}s", s_pollInterval.TotalSeconds);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var dispatched = await mediator.Send(
                    new RunDueScheduledWorkflowsCommand(DateTime.UtcNow), stoppingToken);
                if (dispatched > 0)
                    _logger.LogInformation("WorkflowScheduler 分发 {Count} 个到期工作流", dispatched);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常关闭，退出。
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "WorkflowScheduler 轮询失败（下一轮重试）");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
