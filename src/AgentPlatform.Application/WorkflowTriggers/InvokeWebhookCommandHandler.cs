using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AuditActionType = AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class InvokeWebhookCommandHandler
    : IRequestHandler<InvokeWebhookCommand, TriggerRunResult?>
{
    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IMediator _mediator;

    public InvokeWebhookCommandHandler(
        IWorkflowTriggerRepository triggerRepo,
        IAuditLogRepository auditLogRepository,
        IMediator mediator)
    {
        _triggerRepo = triggerRepo;
        _auditLogRepository = auditLogRepository;
        _mediator = mediator;
    }

    public async Task<TriggerRunResult?> Handle(InvokeWebhookCommand request, CancellationToken ct)
    {
        // 跨租户解析（IgnoreQueryFilters）：匿名端点无租户上下文。
        var trigger = await _triggerRepo.GetByTokenAsync(request.Token, ct);
        if (trigger is null || trigger.Type != TriggerType.Webhook)
            return null; // 404

        if (!trigger.Enabled)
            return null; // 已禁用（控制器映射为 404，与未知 token 一致，不暴露存在性）

        // 审计（基于触发器所属租户）。注意：实际编排在 TriggerWorkflowCommand 内再次校验归属。
        _auditLogRepository.Add(AuditLog.Record(
            trigger.TenantId, AuditActionType.WebhookInvoke, "WorkflowTrigger",
            entityId: trigger.Id, details: "Webhook received (pre-dispatch)"));

        return await _mediator.Send(new TriggerWorkflowCommand(
            trigger.WorkflowId, trigger.TenantId, TriggerType.Webhook, request.BodyJson), ct);
    }
}
