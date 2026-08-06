using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.DebugResume;

/// <summary>Continues a debugged workflow to completion from its current state.</summary>
public sealed record DebugResumeCommand(Guid WorkflowId, Guid SessionId, Guid TenantId)
    : ICommand<DebugDtos.DebugResumeResponse>;

internal sealed class DebugResumeCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IOrchestrationPrimitive primitive)
    : IRequestHandler<DebugResumeCommand, DebugDtos.DebugResumeResponse>
{
    public async Task<DebugDtos.DebugResumeResponse> Handle(DebugResumeCommand request, CancellationToken ct)
    {
        var session = await sessionRepo.GetByIdAsync(request.SessionId, ct)
            ?? throw new KeyNotFoundException($"Debug session '{request.SessionId}' was not found.");
        if (session.WorkflowId != request.WorkflowId)
            throw new KeyNotFoundException($"Debug session '{request.SessionId}' does not belong to workflow '{request.WorkflowId}'.");
        var wf = await workflowRepo.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");

        var blackboard = DebugDtos.ToBlackboard(session.GetVariables());
        var finalState = await primitive.DebugResumeAsync(request.WorkflowId, blackboard, ct);

        session.RecordStep(session.CurrentStepOrder, DebugDtos.Map(finalState), blackboard.Entries);
        sessionRepo.Update(session);

        return new DebugDtos.DebugResumeResponse(finalState, blackboard.Entries);
    }
}
