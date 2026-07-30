using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>
/// Snapshots the current definition of a workflow as a new version (version number = latest + 1).
/// Implements <see cref="ICommand{T}"/> so UnitOfWorkBehavior persists the version and audit record.
/// </summary>
/// <param name="WorkflowId">The workflow to snapshot.</param>
/// <param name="TenantId">The tenant that owns the workflow (resolved by the controller).</param>
/// <param name="Note">Optional human note for the version.</param>
public sealed record CreateWorkflowVersionCommand(Guid WorkflowId, Guid TenantId, string? Note = null)
    : ICommand<WorkflowVersionDetail?>;

internal sealed class CreateWorkflowVersionCommandHandler
    : IRequestHandler<CreateWorkflowVersionCommand, WorkflowVersionDetail?>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public CreateWorkflowVersionCommandHandler(
        IWorkflowRepository workflowRepo,
        IWorkflowVersionRepository versionRepo,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _versionRepo = versionRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<WorkflowVersionDetail?> Handle(CreateWorkflowVersionCommand request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        var nextNumber = await _versionRepo.GetLatestVersionNumberAsync(wf.Id, ct) + 1;
        var snapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
        var version = WorkflowVersion.Create(
            id: Guid.NewGuid(),
            workflowId: wf.Id,
            tenantId: wf.TenantId,
            versionNumber: nextNumber,
            name: wf.Name,
            snapshotJson: snapshot.ToJson(),
            createdBy: null,
            note: request.Note);

        _versionRepo.Add(version);
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: wf.TenantId,
            action: AuditActionType.CreateWorkflowVersion,
            entity: "Workflow",
            entityId: wf.Id,
            details: $"Saved version {nextNumber} of workflow '{wf.Name}'"));

        return WorkflowVersionMapper.ToDetail(version, snapshot);
    }
}
