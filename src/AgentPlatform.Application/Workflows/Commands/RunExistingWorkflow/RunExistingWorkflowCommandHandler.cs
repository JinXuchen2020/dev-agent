using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunExistingWorkflow;

internal sealed class RunExistingWorkflowCommandHandler
    : IRequestHandler<RunExistingWorkflowCommand, WorkflowDetailResponse?>
{
    private readonly IWorkflowRepository _repo;
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IAuditLogRepository _auditLogRepository;

    public RunExistingWorkflowCommandHandler(
        IWorkflowRepository repo,
        IOrchestrationPrimitive primitive,
        IAuditLogRepository auditLogRepository)
    {
        _repo = repo;
        _primitive = primitive;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<WorkflowDetailResponse?> Handle(RunExistingWorkflowCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running)
            throw new WorkflowConflictException($"Workflow '{wf.Id}' is already running.");

        // Re-run semantics: if the workflow already finished (Completed/Failed/RolledBack)
        // or was paused, restart it from a clean state. A still-Pending workflow (e.g. a
        // freshly edited draft) needs no reset, and a Running one is rejected above.
        // RunAsync only accepts Pending/Running, so reset any terminal/paused state first.
        if (wf.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
        {
            wf.Reset();
            _repo.Update(wf);
        }

        // The orchestration primitive handles per-step persistence internally.
        var result = await _primitive.RunAsync(wf, request.Preset, ct);

        var auditLog = AuditLog.Record(
            tenantId: result.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RunWorkflow,
            entity: "Workflow",
            entityId: result.Id,
            details: $"Re-ran workflow '{result.Name}'");
        _auditLogRepository.Add(auditLog);

        return GetWorkflowQuery.ToDetailResponse(result);
    }
}
