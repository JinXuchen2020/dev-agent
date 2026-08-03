using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.PublishedWorkflows;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.PublishedWorkflows;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.PublishedWorkflows.Commands.RunPublishedWorkflow;

internal sealed class RunPublishedWorkflowCommandHandler
    : IRequestHandler<RunPublishedWorkflowCommand, RunPublishedWorkflowResponse?>
{
    private readonly IPublishedWorkflowRepository _publishedRepo;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IAuditLogRepository _auditLogRepository;

    public RunPublishedWorkflowCommandHandler(
        IPublishedWorkflowRepository publishedRepo,
        IWorkflowRepository workflowRepo,
        IOrchestrationPrimitive primitive,
        IAuditLogRepository auditLogRepository)
    {
        _publishedRepo = publishedRepo;
        _workflowRepo = workflowRepo;
        _primitive = primitive;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<RunPublishedWorkflowResponse?> Handle(RunPublishedWorkflowCommand request, CancellationToken ct)
    {
        // 按 slug 运行任意已启用发布记录（API 与 MCP 两种表面共用本 handler；
        // 模式差异仅体现在发布清单 tools/list 的筛选，不影响执行）。
        var pw = await _publishedRepo.GetBySlugAsync(request.Slug, ct);
        if (pw is null || !pw.IsEnabled)
            return null; // 404, 不泄露存在性
        if (pw.ApiKeyId.HasValue && pw.ApiKeyId != request.InvokingKeyId)
            return null; // 绑定 Key 不匹配 → 不可达

        // 轻量输入校验（v1）：输入须为合法 JSON 对象；若定义了 InputSchemaJson 且含 required，校验必填字段存在。
        if (!string.IsNullOrWhiteSpace(pw.InputSchemaJson))
            ValidateInputAgainstSchema(request.InputJson, pw.InputSchemaJson);

        var wf = await _workflowRepo.GetByIdAsync(pw.WorkflowId, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 跨租户 / 不存在 → 不可达

        if (wf.CurrentState is WorkflowState.Running)
            throw PublishedWorkflowException.Conflict($"工作流 '{wf.Id}' 正在运行中。");

        // 重跑语义：终态/暂停态先重置为干净状态（RunAsync 仅接受 Pending/Running）。
        if (wf.CurrentState is not (WorkflowState.Pending or WorkflowState.Running))
        {
            wf.Reset();
            _workflowRepo.Update(wf);
        }

        // 外部输入作为工作流初始共享上下文（blackboard）。
        if (!string.IsNullOrWhiteSpace(request.InputJson))
            wf.UpdateContext(request.InputJson);

        var result = await _primitive.RunAsync(wf, OrchestrationPreset.Sequential, ct);

        _auditLogRepository.Add(AuditLog.Record(
            tenantId: result.TenantId,
            action: AgentPlatform.Domain.Aggregates.AuditLogs.AuditActionType.RunWorkflow,
            entity: "Workflow",
            entityId: result.Id,
            details: $"通过已发布端点（slug={pw.Slug}）运行工作流"));

        return new RunPublishedWorkflowResponse(
            WorkflowId: result.Id,
            Slug: pw.Slug,
            Status: result.CurrentState.ToString(),
            Output: result.Context,
            ErrorMessage: result.CurrentState == WorkflowState.Failed
                ? "工作流执行失败，请检查返回上下文或稍后重试。"
                : null);
    }

    private static void ValidateInputAgainstSchema(string? inputJson, string schemaJson)
    {
        JsonElement input;
        try
        {
            using var doc = JsonDocument.Parse(inputJson ?? "{}");
            input = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw PublishedWorkflowException.BadRequest($"输入不是合法 JSON：{ex.Message}");
        }

        if (input.ValueKind != JsonValueKind.Object)
            throw PublishedWorkflowException.BadRequest("输入必须是 JSON 对象。");

        try
        {
            using var sdoc = JsonDocument.Parse(schemaJson);
            var schema = sdoc.RootElement;
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var req in required.EnumerateArray())
                {
                    if (req.ValueKind != JsonValueKind.String) continue;
                    var name = req.GetString()!;
                    if (!input.TryGetProperty(name, out _))
                        throw PublishedWorkflowException.BadRequest($"缺少必填输入字段：{name}。");
                }
            }
        }
        catch (JsonException)
        {
            // schema 自身非法则跳过校验，不阻断调用。
        }
    }
}
