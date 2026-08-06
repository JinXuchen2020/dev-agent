using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.DebugRetryNode;

/// <summary>Re-runs a specific node within a debug session (optionally overriding its config).</summary>
public sealed record DebugRetryNodeCommand(
    Guid WorkflowId, Guid SessionId, Guid NodeId, Guid TenantId, string? OverriddenConfig = null)
    : ICommand<DebugDtos.DebugRetryResponse>;

internal sealed class DebugRetryNodeCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IOrchestrationPrimitive primitive,
    IAuditLogRepository auditLogRepo)
    : IRequestHandler<DebugRetryNodeCommand, DebugDtos.DebugRetryResponse>
{
    public async Task<DebugDtos.DebugRetryResponse> Handle(DebugRetryNodeCommand request, CancellationToken ct)
    {
        var session = await sessionRepo.GetByIdAsync(request.SessionId, ct)
            ?? throw new KeyNotFoundException($"Debug session '{request.SessionId}' was not found.");
        if (session.WorkflowId != request.WorkflowId)
            throw new KeyNotFoundException($"Debug session '{request.SessionId}' does not belong to workflow '{request.WorkflowId}'.");
        var wf = await workflowRepo.GetByIdAsync(request.WorkflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow '{request.WorkflowId}' was not found.");

        // Optional config override applied before re-running the node.
        if (!string.IsNullOrWhiteSpace(request.OverriddenConfig))
        {
            wf.SetNodeConfig(request.NodeId, request.OverriddenConfig!);
            workflowRepo.Update(wf);
        }

        var blackboard = DebugDtos.ToBlackboard(session.GetVariables());
        var result = await primitive.DebugRetryNodeAsync(request.WorkflowId, request.NodeId, blackboard, ct);

        var order = result.Node?.Order ?? session.CurrentStepOrder;
        session.RecordStep(order, DebugDtos.Map(result.WorkflowState), blackboard.Entries);
        sessionRepo.Update(session);

        auditLogRepo.Add(AuditLog.Record(
            request.TenantId, AuditActionType.StepRetry, "Workflow",
            entityId: request.WorkflowId, details: $"Retried node {request.NodeId} in debug session {request.SessionId}"));

        return new DebugDtos.DebugRetryResponse(
            result.Executed, result.WorkflowState, result.Node, blackboard.Entries);
    }
}
