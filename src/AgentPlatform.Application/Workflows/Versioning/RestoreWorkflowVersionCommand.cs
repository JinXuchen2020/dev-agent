using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>
/// Rolls a workflow back to a saved version (graph + name + context). Rejects rollback while the
/// workflow is Running or Paused. Implements <see cref="ICommand{T}"/> so the change is persisted.
/// </summary>
/// <param name="WorkflowId">The workflow to roll back.</param>
/// <param name="VersionId">The version to restore.</param>
/// <param name="TenantId">The tenant that owns the workflow.</param>
public sealed record RestoreWorkflowVersionCommand(Guid WorkflowId, Guid VersionId, Guid TenantId)
    : ICommand<WorkflowDetailResponse?>;

internal sealed class RestoreWorkflowVersionCommandHandler
    : IRequestHandler<RestoreWorkflowVersionCommand, WorkflowDetailResponse?>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public RestoreWorkflowVersionCommandHandler(
        IWorkflowRepository workflowRepo,
        IWorkflowVersionRepository versionRepo,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _versionRepo = versionRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<WorkflowDetailResponse?> Handle(RestoreWorkflowVersionCommand request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null;

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; restore is not allowed until it finishes.");

        var version = await _versionRepo.GetByIdAsync(request.VersionId, ct);
        if (version is null || version.WorkflowId != request.WorkflowId)
            return null;

        var snapshot = WorkflowGraphSnapshot.FromJson(version.SnapshotJson);
        var (nodes, edges) = snapshot.ToReplaceGraphArgs();
        wf.Rename(version.Name);
        wf.UpdateContext(snapshot.Context);
        wf.ReplaceGraph(nodes, edges);

        _workflowRepo.Update(wf);
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: wf.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RestoreWorkflowVersion,
            entity: "Workflow",
            entityId: wf.Id,
            details: $"Restored workflow '{wf.Name}' to version {version.VersionNumber}"));

        return GetWorkflowQuery.ToDetailResponse(wf);
    }
}
