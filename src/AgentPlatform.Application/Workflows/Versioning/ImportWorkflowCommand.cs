using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Commands.UpdateWorkflow;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>
/// Imports a workflow definition as a NEW workflow (never overwrites an existing one).
/// The graph is validated by <see cref="Workflow.ReplaceGraph"/>. Implements <see cref="ICommand{T}"/>
/// so the created workflow and audit record are persisted.
/// </summary>
/// <param name="Name">The display name for the new workflow.</param>
/// <param name="InitialContext">The shared context JSON.</param>
/// <param name="Nodes">Optional graph nodes (whole-graph definition).</param>
/// <param name="Edges">Optional graph edges.</param>
/// <param name="TenantId">The tenant that will own the new workflow.</param>
public sealed record ImportWorkflowCommand(
    string Name,
    string InitialContext,
    IReadOnlyList<WorkflowNodeRequest>? Nodes = null,
    IReadOnlyList<WorkflowEdgeRequest>? Edges = null,
    Guid TenantId = default)
    : ICommand<WorkflowDetailResponse>;

internal sealed class ImportWorkflowCommandHandler : IRequestHandler<ImportWorkflowCommand, WorkflowDetailResponse>
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IAuditLogRepository _auditLogRepository;

    public ImportWorkflowCommandHandler(
        IWorkflowRepository workflowRepo,
        IAuditLogRepository auditLogRepository)
    {
        _workflowRepo = workflowRepo;
        _auditLogRepository = auditLogRepository;
    }

    public Task<WorkflowDetailResponse> Handle(ImportWorkflowCommand request, CancellationToken ct)
    {
        var wf = new Workflow(Guid.NewGuid(), request.Name, request.TenantId);
        if (!string.IsNullOrWhiteSpace(request.InitialContext))
            wf.UpdateContext(request.InitialContext);

        if (request.Nodes is { Count: > 0 })
        {
            wf.ReplaceGraph(
                request.Nodes.Select(n => (n.Id, n.Type, n.Name, n.Position.X, n.Position.Y, n.Config, n.AssignedAgentId)).ToList(),
                request.Edges?
                    .Select(e => (e.Id, e.Source, e.Target, e.Label))
                    .ToList()
                    ?? new List<(Guid, Guid, Guid, string?)>());
        }

        _workflowRepo.Add(wf);
        _auditLogRepository.Add(AuditLog.Record(
            tenantId: wf.TenantId,
            action: AuditActionType.ImportWorkflow,
            entity: "Workflow",
            entityId: wf.Id,
            details: $"Imported workflow '{wf.Name}'"));

        return Task.FromResult(GetWorkflowQuery.ToDetailResponse(wf));
    }
}
