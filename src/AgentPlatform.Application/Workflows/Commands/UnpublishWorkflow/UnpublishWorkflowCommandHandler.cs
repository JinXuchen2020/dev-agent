using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.UnpublishWorkflow;

internal sealed class UnpublishWorkflowCommandHandler
    : IRequestHandler<UnpublishWorkflowCommand, Unit>
{
    private readonly IPublishedWorkflowRepository _publishedRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public UnpublishWorkflowCommandHandler(
        IPublishedWorkflowRepository publishedRepo,
        IAuditLogRepository auditLogRepository)
    {
        _publishedRepo = publishedRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<Unit> Handle(UnpublishWorkflowCommand request, CancellationToken ct)
    {
        var existing = await _publishedRepo.GetByWorkflowIdAsync(request.TenantId, request.WorkflowId, ct);
        if (existing is not null)
        {
            _publishedRepo.Delete(existing);
            _auditLogRepository.Add(AuditLog.Record(
                tenantId: request.TenantId,
                action: AuditActionType.UnpublishWorkflow,
                entity: "Workflow",
                entityId: request.WorkflowId,
                details: $"取消发布（slug={existing.Slug}）"));
        }

        return Unit.Value;
    }
}
