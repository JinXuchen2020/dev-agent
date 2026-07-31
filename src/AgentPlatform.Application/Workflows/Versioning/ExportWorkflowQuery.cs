using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>Exports the current definition of a workflow as JSON (re-importable shape).</summary>
/// <param name="WorkflowId">The workflow to export.</param>
public sealed record ExportWorkflowQuery(Guid WorkflowId) : IRequest<WorkflowExport?>;

internal sealed class ExportWorkflowQueryHandler : IRequestHandler<ExportWorkflowQuery, WorkflowExport?>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ExportWorkflowQueryHandler(
        IWorkflowRepository workflowRepo,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork)
    {
        _workflowRepo = workflowRepo;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<WorkflowExport?> Handle(ExportWorkflowQuery request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null)
            return null;

        var export = new WorkflowExport(
            wf.Id,
            wf.Name,
            wf.Context,
            wf.Nodes.Select(n => new WorkflowNodeRequest(
                n.Id, n.Type, n.Name, new WorkflowNodePosition(n.PositionX, n.PositionY),
                n.ConfigJson, n.AssignedAgentId)).ToList(),
            wf.Edges.Select(e => new WorkflowEdgeRequest(
                e.Id, e.SourceNodeId, e.TargetNodeId, e.Label)).ToList(),
            DateTime.UtcNow);

        // Export is a read (query), so the audit record is persisted explicitly rather than
        // relying on UnitOfWorkBehavior (which only auto-commits ICommand handlers).
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: wf.TenantId,
            action: AuditActionType.ExportWorkflow,
            entity: "Workflow",
            entityId: wf.Id,
            details: $"Exported workflow '{wf.Name}'"));
        await _unitOfWork.SaveChangesAsync(ct);

        return export;
    }
}
