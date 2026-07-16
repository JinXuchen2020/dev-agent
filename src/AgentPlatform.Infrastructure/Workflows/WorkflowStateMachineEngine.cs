using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.Workflows.Events;
using AgentPlatform.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Infrastructure.Workflows;

/// <summary>
/// Legacy engine — fully replaced by <see cref="OrchestrationPrimitive"/> (Blueprint C.2).
/// This file is kept for reference and removed from DI registration.
/// All new orchestration goes through OrchestrationPrimitive with preset routing.
/// </summary>
[Obsolete("Replaced by OrchestrationPrimitive. Use IOrchestrationPrimitive with OrchestrationPreset.Sequential instead.")]
internal sealed class WorkflowStateMachineEngine
{
    public WorkflowStateMachineEngine(
        IEnumerable<IStepExecutor> executors,
        IOptions<StateMachineSettings> settings,
        ILogger<WorkflowStateMachineEngine> logger,
        IDomainEventBus eventBus)
    {
        // No-op: logic migrated to OrchestrationPrimitive.RunSequentialAsync
    }
}
