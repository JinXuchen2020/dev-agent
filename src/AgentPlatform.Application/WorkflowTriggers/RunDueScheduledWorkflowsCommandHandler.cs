using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class RunDueScheduledWorkflowsCommandHandler
    : IRequestHandler<RunDueScheduledWorkflowsCommand, int>
{
    private static readonly TimeSpan s_lockExpiry = TimeSpan.FromMinutes(5);

    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IScheduleCalculator _calculator;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RunDueScheduledWorkflowsCommandHandler> _logger;

    public RunDueScheduledWorkflowsCommandHandler(
        IWorkflowTriggerRepository triggerRepo,
        IScheduleCalculator calculator,
        IDistributedLockProvider lockProvider,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ILogger<RunDueScheduledWorkflowsCommandHandler> logger)
    {
        _triggerRepo = triggerRepo;
        _calculator = calculator;
        _lockProvider = lockProvider;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<int> Handle(RunDueScheduledWorkflowsCommand request, CancellationToken ct)
    {
        var due = await _triggerRepo.GetDueSchedulesAsync(request.NowUtc, ct);
        var dispatched = 0;

        foreach (var trigger in due)
        {
            // 多实例防重：获取该触发器专属锁；获取失败（其他副本持有）则跳过。
            var lockKey = $"trigger:{trigger.Id}";
            var acquired = await _lockProvider.TryAcquireAsync(lockKey, s_lockExpiry, ct);
            if (!acquired)
            {
                _logger.LogDebug("跳过触发器 {TriggerId}：锁已被其他实例持有", trigger.Id);
                continue;
            }

            try
            {
                // 重算并持久化下次运行时间（即使本次编排失败，调度状态仍向前推进，避免死循环重触发）。
                var nextRunAt = _calculator.ComputeNextRunUtc(trigger.Cron!, trigger.Timezone!, request.NowUtc);
                trigger.MarkScheduledRun(request.NowUtc, nextRunAt);
                _triggerRepo.Update(trigger);

                // 注入租户，使 TriggerWorkflowCommand 的 DbContext 过滤器落到正确租户。
                _tenantContext.OverrideTenantId = trigger.TenantId;
                await _unitOfWork.SaveChangesAsync(ct);

                await _mediator.Send(new TriggerWorkflowCommand(
                    trigger.WorkflowId, trigger.TenantId, TriggerType.Schedule, null), ct);
                dispatched++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调度触发器 {TriggerId} 执行失败", trigger.Id);
            }
            finally
            {
                await _lockProvider.ReleaseAsync(lockKey, ct);
            }
        }

        return dispatched;
    }
}
