using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.DebugStep;

/// <summary>Runs the next Pending node of a debugged workflow, then pauses.</summary>
public sealed record DebugStepCommand(Guid WorkflowId, Guid SessionId, Guid TenantId)
    : ICommand<DebugDtos.DebugStepResponse>;

internal sealed class DebugStepCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IOrchestrationPrimitive primitive)
    : IRequestHandler<DebugStepCommand, DebugDtos.DebugStepResponse>
{
    public async Task<DebugDtos.DebugStepResponse> Handle(DebugStepCommand request, CancellationToken ct)
    {
        var session = await sessionRepo.GetByIdAsync(request.SessionId, ct)
            ?? throw new KeyNotFoundException($"Debug session '{request.SessionId}' was not found.");
        if (session.WorkflowId != request.WorkflowId)
            throw new KeyNotFoundException($"Debug session '{request.SessionId}' does not belong to workflow '{request.WorkflowId}'.");
        var wf = await workflowRepo.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");

        var blackboard = DebugDtos.ToBlackboard(session.GetVariables());
        var result = await primitive.DebugStepAsync(request.WorkflowId, blackboard, ct);

        var order = result.Node?.Order ?? session.CurrentStepOrder;
        session.RecordStep(order, DebugDtos.Map(result.WorkflowState), blackboard.Entries);
        sessionRepo.Update(session);

        return new DebugDtos.DebugStepResponse(
            result.Executed, result.WorkflowState, result.Node, blackboard.Entries);
    }
}
