using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;

internal sealed class UpdateWorkflowCommandHandler
    : IRequestHandler<UpdateWorkflowCommand, WorkflowDetailResponse?>
{
    private readonly IWorkflowRepository _repo;

    public UpdateWorkflowCommandHandler(IWorkflowRepository repo)
    {
        _repo = repo;
    }

    public async Task<WorkflowDetailResponse?> Handle(UpdateWorkflowCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; edits are not allowed until it finishes.");

        if (!string.IsNullOrWhiteSpace(request.Name))
            wf.Rename(request.Name);
        if (!string.IsNullOrWhiteSpace(request.InitialContext))
            wf.UpdateContext(request.InitialContext);
        if (request.Steps is { Count: > 0 })
            wf.ReplaceSteps(request.Steps);

        _repo.Update(wf); // tracked entity; UnitOfWorkBehavior commits

        return ToDetailResponse(wf);
    }

    internal static WorkflowDetailResponse ToDetailResponse(Workflow wf) => new(
        wf.Id,
        wf.Name,
        wf.CurrentState,
        wf.Steps.Select(s => new WorkflowStepResponse(
            s.Id, s.Order, s.StepName, s.AssignedAgentId, s.State, s.Result, s.ErrorDetail)).ToList(),
        wf.Context,
        wf.CreatedAt,
        wf.UpdatedAt);
}
