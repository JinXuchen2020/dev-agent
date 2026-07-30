using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>
/// Deletes a workflow version. Idempotent: a missing or mismatched version is treated as already gone.
/// Implements <see cref="ICommand"/> so the removal is persisted via UnitOfWorkBehavior.
/// </summary>
/// <param name="WorkflowId">The owning workflow.</param>
/// <param name="VersionId">The version to delete.</param>
/// <param name="TenantId">The tenant that owns the workflow.</param>
public sealed record DeleteWorkflowVersionCommand(Guid WorkflowId, Guid VersionId, Guid TenantId)
    : ICommand;

internal sealed class DeleteWorkflowVersionCommandHandler
    : IRequestHandler<DeleteWorkflowVersionCommand>
{
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public DeleteWorkflowVersionCommandHandler(
        IWorkflowVersionRepository versionRepo,
        IAuditLogRepository auditLogRepository)
    {
        _versionRepo = versionRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task Handle(DeleteWorkflowVersionCommand request, CancellationToken ct)
    {
        var version = await _versionRepo.GetByIdAsync(request.VersionId, ct);
        if (version is null || version.WorkflowId != request.WorkflowId || version.TenantId != request.TenantId)
            return; // already gone / not ours → idempotent

        _versionRepo.Remove(version);
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: version.TenantId,
            action: AuditActionType.DeleteWorkflowVersion,
            entity: "Workflow",
            entityId: version.WorkflowId,
            details: $"Deleted version {version.VersionNumber}"));
    }
}
