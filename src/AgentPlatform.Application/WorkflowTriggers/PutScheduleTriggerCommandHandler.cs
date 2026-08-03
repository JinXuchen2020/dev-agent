using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AuditActionType = AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType;
using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class PutScheduleTriggerCommandHandler
    : IRequestHandler<PutScheduleTriggerCommand, ScheduleTriggerResult?>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IScheduleCalculator _calculator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _auditLogRepository;

    public PutScheduleTriggerCommandHandler(
        IWorkflowRepository workflowRepo,
        IWorkflowTriggerRepository triggerRepo,
        IScheduleCalculator calculator,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _triggerRepo = triggerRepo;
        _calculator = calculator;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<ScheduleTriggerResult?> Handle(PutScheduleTriggerCommand request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null;

        var nowUtc = DateTime.UtcNow;
        var nextRunAt = request.Enabled
            ? _calculator.ComputeNextRunUtc(request.Cron, request.Timezone, nowUtc)
            : null;

        var existing = await _triggerRepo.GetByWorkflowAndTypeAsync(request.WorkflowId, TriggerType.Schedule, ct);
        if (existing is null)
        {
            var trigger = WorkflowTrigger.CreateSchedule(
                Guid.NewGuid(), request.WorkflowId, request.TenantId,
                request.Cron, request.Timezone, request.Enabled, nextRunAt);
            _triggerRepo.Add(trigger);
            _auditLogRepository.Add(AuditLog.Record(
                request.TenantId, AuditActionType.EnableTrigger, "WorkflowTrigger",
                entityId: trigger.Id,
                details: $"Created schedule trigger (cron={request.Cron}, tz={request.Timezone}, enabled={request.Enabled})"));
            await _unitOfWork.SaveChangesAsync(ct);
            return new ScheduleTriggerResult(trigger.Id, request.Cron, request.Timezone, request.Enabled, nextRunAt);
        }

        existing.UpdateSchedule(request.Cron, request.Timezone, request.Enabled, nextRunAt);
        _triggerRepo.Update(existing);
        _auditLogRepository.Add(AuditLog.Record(
            request.TenantId,
            request.Enabled ? AuditActionType.EnableTrigger : AuditActionType.DisableTrigger,
            "WorkflowTrigger",
            entityId: existing.Id,
            details: $"Updated schedule trigger (cron={request.Cron}, tz={request.Timezone}, enabled={request.Enabled})"));
        await _unitOfWork.SaveChangesAsync(ct);
        return new ScheduleTriggerResult(existing.Id, request.Cron, request.Timezone, request.Enabled, nextRunAt);
    }
}
