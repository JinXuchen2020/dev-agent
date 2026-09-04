using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;

/// <summary>
/// Re-runs an existing workflow by id, reusing the same aggregate (does NOT create a duplicate).
/// Does NOT implement <see cref="ICommand{T}"/> because <see cref="Abstractions.IOrchestrationPrimitive.RunAsync"/>
/// manages its own per-step persistence; routing it through UnitOfWorkBehavior would double-save.
/// </summary>
/// <param name="Id">The existing workflow identifier.</param>
/// <param name="Preset">Orchestration preset (sequential = fast path, negotiation = critic loop).</param>
/// <param name="TenantId">The tenant that owns the workflow (resolved by the controller).</param>
/// <param name="RequestingUserId">F37：发起用户（审计归属，可空；队列模式随作业载荷传递）。</param>
public record RunExistingWorkflowCommand(
    Guid Id,
    OrchestrationPreset Preset = OrchestrationPreset.Sequential,
    Guid TenantId = default,
    Guid? RequestingUserId = null
) : IRequest<ExistingWorkflowRunResult?>;  // F37 D2=B：统一直跑/队列结果
