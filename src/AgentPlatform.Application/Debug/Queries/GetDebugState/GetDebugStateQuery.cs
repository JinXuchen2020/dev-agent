using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Queries.GetDebugState;

/// <summary>Returns the current execution state snapshot of a workflow (node states/results).</summary>
public sealed record GetDebugStateQuery(Guid WorkflowId, Guid TenantId)
    : IRequest<WorkflowStateSnapshot>;

internal sealed class GetDebugStateQueryHandler(
    IWorkflowRepository workflowRepo,
    IOrchestrationPrimitive primitive)
    : IRequestHandler<GetDebugStateQuery, WorkflowStateSnapshot>
{
    public async Task<WorkflowStateSnapshot> Handle(GetDebugStateQuery request, CancellationToken ct)
    {
        var wf = await workflowRepo.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");
        return await primitive.GetStateAsync(request.WorkflowId, ct);
    }
}
