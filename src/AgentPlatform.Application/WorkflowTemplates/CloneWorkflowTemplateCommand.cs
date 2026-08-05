using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Aggregates.WorkflowTemplates;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.WorkflowTemplates;

/// <summary>
/// Clones a platform template into a new <see cref="Workflow"/> owned by the caller's tenant.
/// Reuses F7 ①'s <see cref="WorkflowGraphSnapshot"/> + <see cref="Workflow.ReplaceGraph"/> to rebuild
/// the graph. Per 决策 S3, node agent assignments are intentionally dropped (platform templates do not
/// bind any tenant's agents), so the clone is agent-agnostic and the user assigns agents before running.
/// </summary>
/// <param name="TemplateId">The template to clone.</param>
/// <param name="TenantId">The tenant that will own the new workflow.</param>
public sealed record CloneWorkflowTemplateCommand(Guid TemplateId, Guid TenantId)
    : ICommand<WorkflowDetailResponse?>;

internal sealed class CloneWorkflowTemplateCommandHandler
    : IRequestHandler<CloneWorkflowTemplateCommand, WorkflowDetailResponse?>
{
    private readonly IWorkflowTemplateRepository _templateRepo;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public CloneWorkflowTemplateCommandHandler(
        IWorkflowTemplateRepository templateRepo,
        IWorkflowRepository workflowRepo,
        IAuditLogRepository auditLogRepository)
    {
        _templateRepo = templateRepo;
        _workflowRepo = workflowRepo;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<WorkflowDetailResponse?> Handle(CloneWorkflowTemplateCommand request, CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(request.TemplateId, ct);
        if (template is null)
            return null;

        var snapshot = WorkflowGraphSnapshot.FromJson(template.SnapshotJson);
        var (sourceNodes, edges) = snapshot.ToReplaceGraphArgs();

        // 决策 S3：克隆出的工作流节点不预绑 Agent（平台模板不引用任何租户 Agent）。
        var nodes = sourceNodes
            .Select(n => (n.TempId, n.Type, n.Name, n.X, n.Y, n.Config, (Guid?)null))
            .ToList();

        var workflow = new Workflow(Guid.NewGuid(), $"{template.Name} (副本)", request.TenantId);
        workflow.ReplaceGraph(nodes, edges); // ValidateGraph 在内部强制（种子快照均为合法图）

        _workflowRepo.Add(workflow);
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: request.TenantId,
            action: AuditActionType.CloneTemplate,
            entity: "Workflow",
            entityId: workflow.Id,
            details: $"Cloned workflow from template '{template.Name}' (templateId={template.Id})"));

        return GetWorkflowQuery.ToDetailResponse(workflow);
    }
}
