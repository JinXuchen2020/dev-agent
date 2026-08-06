using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Debug.Commands.StartDebugSession;

/// <summary>Starts a fresh debug session for a workflow (resets node states).</summary>
public sealed record StartDebugSessionCommand(Guid WorkflowId, Guid TenantId, string? InitialContext = null)
    : ICommand<DebugDtos.StartDebugSessionResponse>;

internal sealed class StartDebugSessionCommandHandler(
    IWorkflowRepository workflowRepo,
    IDebugSessionRepository sessionRepo,
    IAuditLogRepository auditLogRepo)
    : IRequestHandler<StartDebugSessionCommand, DebugDtos.StartDebugSessionResponse>
{
    public async Task<DebugDtos.StartDebugSessionResponse> Handle(StartDebugSessionCommand request, CancellationToken ct)
        => await DebugDtos.StartOrResetAsync(
            request.WorkflowId, request.TenantId, request.InitialContext,
            workflowRepo, sessionRepo, auditLogRepo, ct);
}
