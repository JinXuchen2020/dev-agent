using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>
/// 内部命令：由 <c>WorkflowScheduler</c> 后台服务调用，扫描所有租户中到期的 Schedule 触发器，
/// 在分布式锁保护下逐个重算下次运行并委托 <see cref="TriggerWorkflowCommand"/> 启动编排。
/// </summary>
/// <param name="NowUtc">扫描基准 UTC 时间（由调度器传入，便于测试）。</param>
public record RunDueScheduledWorkflowsCommand(DateTime NowUtc) : IRequest<int>;
