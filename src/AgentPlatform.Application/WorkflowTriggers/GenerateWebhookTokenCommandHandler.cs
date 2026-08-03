using System.Security.Cryptography;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AuditActionType = AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType;
using AgentPlatform.Domain.Aggregates.WorkflowTriggers;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

internal sealed class GenerateWebhookTokenCommandHandler
    : IRequestHandler<GenerateWebhookTokenCommand, WebhookTokenResult?>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IWorkflowTriggerRepository _triggerRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _auditLogRepository;

    public GenerateWebhookTokenCommandHandler(
        IWorkflowRepository workflowRepo,
        IWorkflowTriggerRepository triggerRepo,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _triggerRepo = triggerRepo;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<WebhookTokenResult?> Handle(GenerateWebhookTokenCommand request, CancellationToken ct)
    {
        var wf = await _workflowRepo.GetByIdAsync(request.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null;

        // 幂等：已存在则复用现有令牌（不轮换，避免误调用导致旧令牌失效），并确保启用；
        // 不存在则生成新令牌并创建启用态触发器。
        var existing = await _triggerRepo.GetByWorkflowAndTypeAsync(request.WorkflowId, TriggerType.Webhook, ct);

        if (existing is null)
        {
            var token = GenerateToken();
            var trigger = WorkflowTrigger.CreateWebhook(Guid.NewGuid(), request.WorkflowId, request.TenantId, token);
            _triggerRepo.Add(trigger);
            _auditLogRepository.Add(AuditLog.Record(
                request.TenantId, AuditActionType.EnableTrigger, "WorkflowTrigger",
                entityId: trigger.Id, details: $"Created webhook trigger for workflow '{wf.Name}'"));
            await _unitOfWork.SaveChangesAsync(ct);
            return new WebhookTokenResult(trigger.Id, token, true);
        }

        // 已存在：若被禁用则重新启用（保留原令牌），幂等返回现有令牌。
        if (!existing.Enabled)
        {
            existing.SetEnabled(true);
            _triggerRepo.Update(existing);
            _auditLogRepository.Add(AuditLog.Record(
                request.TenantId, AuditActionType.EnableTrigger, "WorkflowTrigger",
                entityId: existing.Id, details: $"Re-enabled webhook trigger for workflow '{wf.Name}'"));
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return new WebhookTokenResult(existing.Id, existing.TriggerToken!, false);
    }

    internal static string GenerateToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        // URL-safe base64（去填充），不可猜。
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
