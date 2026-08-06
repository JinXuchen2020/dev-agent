using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.DebugRollback;

/// <summary>Rolls a debugged workflow back to a target step order (precise rollback).</summary>
public sealed record DebugRollbackCommand(Guid WorkflowId, Guid SessionId, int TargetStepOrder, Guid TenantId)
    : ICommand<DebugDtos.DebugResumeResponse>;

internal sealed class DebugRollbackCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IOrchestrationPrimitive primitive)
    : IRequestHandler<DebugRollbackCommand, DebugDtos.DebugResumeResponse>
{
    public async Task<DebugDtos.DebugResumeResponse> Handle(DebugRollbackCommand request, CancellationToken ct)
    {
        var session = await sessionRepo.GetByIdAsync(request.SessionId, ct)
            ?? throw new KeyNotFoundException($"Debug session '{request.SessionId}' was not found.");
        if (session.WorkflowId != request.WorkflowId)
            throw new KeyNotFoundException($"Debug session '{request.SessionId}' does not belong to workflow '{request.WorkflowId}'.");
        var wf = await workflowRepo.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");

        await primitive.RollbackToAsync(request.WorkflowId, request.TargetStepOrder, ct);

        session.RecordStep(request.TargetStepOrder, DebugDtos.Map(wf.CurrentState), session.GetVariables());
        sessionRepo.Update(session);

        return new DebugDtos.DebugResumeResponse(wf.CurrentState, session.GetVariables());
    }
}
