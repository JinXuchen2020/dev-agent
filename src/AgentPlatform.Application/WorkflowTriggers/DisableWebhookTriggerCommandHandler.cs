using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;
using AuditActionType = AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class DisableWebhookTriggerCommandHandler
    : IRequestHandler<DisableWebhookTriggerCommand, bool>
{
    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _auditLogRepository;

    public DisableWebhookTriggerCommandHandler(
        IWorkflowTriggerRepository triggerRepo,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository)
    {
        _triggerRepo = triggerRepo;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<bool> Handle(DisableWebhookTriggerCommand request, CancellationToken ct)
    {
        var trigger = await _triggerRepo.GetByWorkflowAndTypeAsync(request.WorkflowId, TriggerType.Webhook, ct);
        if (trigger is null)
            return true; // 幂等：本无 webhook 触发器

        trigger.SetEnabled(false);
        _triggerRepo.Update(trigger);
        _auditLogRepository.Add(AuditLog.Record(
            request.TenantId, AuditActionType.DisableTrigger, "WorkflowTrigger",
            entityId: trigger.Id, details: "Revoked webhook trigger"));
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}
