using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.ResetDebugSession;

/// <summary>Resets the debug session and workflow to a clean slate (reuses start logic).</summary>
public sealed record ResetDebugSessionCommand(Guid WorkflowId, Guid TenantId)
    : ICommand<DebugDtos.StartDebugSessionResponse>;

internal sealed class ResetDebugSessionCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IAuditLogRepository auditLogRepo)
    : IRequestHandler<ResetDebugSessionCommand, DebugDtos.StartDebugSessionResponse>
{
    public async Task<DebugDtos.StartDebugSessionResponse> Handle(ResetDebugSessionCommand request, CancellationToken ct)
        => await DebugDtos.StartOrResetAsync(
            request.WorkflowId, request.TenantId, null,
            workflowRepo, sessionRepo, auditLogRepo, ct);
}
