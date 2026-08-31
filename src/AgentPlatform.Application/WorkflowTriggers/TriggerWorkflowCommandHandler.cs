using System.Text.Json;
using System.Text.Json.Nodes;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AuditActionType = AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>
/// 触发器运行处理器：在 scope 内注入租户、合并触发器信封到共享 Context、以 Sequential 预设启动编排，
/// 运行结束后还原工作流原始 Context（避免触发载荷被持久化进工作流配置）。
/// </summary>
internal sealed class TriggerWorkflowCommandHandler
    : IRequestHandler<TriggerWorkflowCommand, TriggerRunResult?>
{
    private readonly IWorkflowRepository _repo;
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IWorkspaceDirectory _workspaceDirectory;

    public TriggerWorkflowCommandHandler(
        IWorkflowRepository repo,
        IOrchestrationPrimitive primitive,
        IUnitOfWork unitOfWork,
        IAuditLogRepository auditLogRepository,
        ITenantContext tenantContext,
        IWorkspaceContext workspaceContext,
        IWorkspaceDirectory workspaceDirectory)
    {
        _repo = repo;
        _primitive = primitive;
        _unitOfWork = unitOfWork;
        _auditLogRepository = auditLogRepository;
        _tenantContext = tenantContext;
        _workspaceContext = workspaceContext;
        _workspaceDirectory = workspaceDirectory;
    }

    public async Task<TriggerRunResult?> Handle(TriggerWorkflowCommand request, CancellationToken ct)
    {
        // 后台调度 / 匿名 Webhook 无 HTTP 租户上下文：显式注入，使 RunAsync 的租户解析落到正确租户。
        _tenantContext.OverrideTenantId = request.TenantId;
        // F35：同步注入工作空间上下文（v1 语义 = 触发执行落在租户默认工作空间，见设计文档已知限制）。
        // 注意：请求 scope 的 AppDbContext 在处理器构造前即已捕获过滤器值，此注入不影响查询过滤器。
        _workspaceContext.OverrideWorkspaceId = _workspaceDirectory.GetDefaultWorkspaceId(request.TenantId);

        // 触发器定位仅按租户（GetByIdForTriggerAsync）：scope 的工作空间上下文恒为租户默认工作空间，
        // 若沿用工作空间过滤，非默认工作空间的工作流会被静默跳过（永不触发）。
        var wf = await _repo.GetByIdForTriggerAsync(request.WorkflowId, request.TenantId, ct);
        if (wf is null)
            return null; // 404，不暴露存在性

        if (wf.CurrentState is WorkflowState.Running)
            return null; // 已在运行，避免并发冲突（调度/Webhook 直接跳过）

        // 终态 / 暂停态需重置为干净状态后再跑（RunAsync 仅接受 Pending/Running）。
        if (wf.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
        {
            wf.Reset();
            _repo.Update(wf);
        }

        // 合并触发器信封到共享 Context（运行时由编排器落入 Blackboard；运行后还原）。
        var originalContext = wf.Context;
        var envelope = BuildTriggerEnvelope(request.TriggerType, request.PayloadJson);
        wf.UpdateContext(MergeTriggerEnvelope(originalContext, envelope));

        var run = await _primitive.RunAsync(wf, OrchestrationPreset.Sequential, ct);

        var auditAction = request.TriggerType switch
        {
            TriggerType.Webhook => AuditActionType.WebhookInvoke,
            TriggerType.Schedule => AuditActionType.ScheduledRun,
            _ => AuditActionType.RunWorkflow
        };
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: run.TenantId,
            action: auditAction,
            entity: "Workflow",
            entityId: run.Id,
            details: $"Triggered ({request.TriggerType}) workflow '{run.Name}'"));

        // 还原工作流原始 Context，避免触发载荷被持久化进工作流配置。
        wf.UpdateContext(originalContext);
        _repo.Update(wf);
        await _unitOfWork.SaveChangesAsync(ct);

        return new TriggerRunResult(run.Id, run.Name, run.CurrentState);
    }

    private static string BuildTriggerEnvelope(TriggerType type, string? payloadJson)
    {
        var root = new JsonObject
        {
            ["type"] = type.ToString().ToLowerInvariant(),
            ["firedAt"] = DateTime.UtcNow.ToString("O")
        };

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                root["payload"] = JsonNode.Parse(payloadJson);
            }
            catch (JsonException)
            {
                root["payload"] = payloadJson; // 非 JSON 体原样存为字符串
            }
        }
        else
        {
            root["payload"] = null;
        }

        return root.ToJsonString();
    }

    private static string MergeTriggerEnvelope(string originalContext, string triggerEnvelopeJson)
    {
        if (string.IsNullOrWhiteSpace(originalContext))
            return $"{{\"trigger\":{triggerEnvelopeJson}}}";

        try
        {
            using var doc = JsonDocument.Parse(originalContext);
            var obj = JsonNode.Parse(originalContext)?.AsObject()
                      ?? new JsonObject();
            obj["trigger"] = JsonNode.Parse(triggerEnvelopeJson);
            return obj.ToJsonString();
        }
        catch (JsonException)
        {
            // 原 Context 非合法 JSON：以触发器信封为唯一内容。
            return $"{{\"trigger\":{triggerEnvelopeJson}}}";
        }
    }
}
