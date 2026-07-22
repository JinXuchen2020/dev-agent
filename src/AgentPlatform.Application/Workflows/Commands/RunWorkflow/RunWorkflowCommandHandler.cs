using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunWorkflow;

internal sealed class RunWorkflowCommandHandler : IRequestHandler<RunWorkflowCommand, Workflow>
{
    private readonly IOrchestrationPrimitive _primitive;
    private readonly IAuditLogRepository _auditLogRepository;

    public RunWorkflowCommandHandler(
        IOrchestrationPrimitive primitive,
        IAuditLogRepository auditLogRepository)
    {
        _primitive = primitive;
        _auditLogRepository = auditLogRepository;
    }

    public async Task<Workflow> Handle(RunWorkflowCommand request, CancellationToken ct)
    {
        var workflow = new Workflow(Guid.NewGuid(), request.Name, request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.InitialContext))
        {
            workflow.UpdateContext(request.InitialContext);
        }

        // Create steps from the request if provided (Blueprint C.2: sequential preset)
        if (request.Steps is { Count: > 0 })
        {
            for (var i = 0; i < request.Steps.Count; i++)
            {
                workflow.AddStep(new WorkflowStep(Guid.NewGuid(), i, request.Steps[i]));
            }
        }

        // The orchestration primitive handles per-step persistence internally
        var result = await _primitive.RunAsync(workflow, request.Preset, ct);

        var auditLog = AuditLog.Record(
            tenantId: result.TenantId,
            action: AuditActionType.RunWorkflow,
            entity: "Workflow",
            entityId: result.Id,
            details: $"Started workflow '{result.Name}'");
        _auditLogRepository.Add(auditLog);

        return result;
    }
}
