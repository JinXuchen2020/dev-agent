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
public record RunExistingWorkflowCommand(
    Guid Id,
    OrchestrationPreset Preset = OrchestrationPreset.Sequential,
    Guid TenantId = default
) : IRequest<WorkflowDetailResponse?>;
