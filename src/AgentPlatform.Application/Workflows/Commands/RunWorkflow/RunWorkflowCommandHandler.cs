using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunWorkflow;

internal sealed class RunWorkflowCommandHandler : IRequestHandler<RunWorkflowCommand, Workflow>
{
    private readonly IOrchestrationPrimitive _primitive;

    public RunWorkflowCommandHandler(IOrchestrationPrimitive primitive)
    {
        _primitive = primitive;
    }

    public async Task<Workflow> Handle(RunWorkflowCommand request, CancellationToken ct)
    {
        var workflow = new Workflow(Guid.NewGuid(), request.Name, request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.InitialContext))
        {
            workflow.UpdateContext(request.InitialContext);
        }

        // The orchestration primitive handles per-step persistence internally
        return await _primitive.RunAsync(workflow, request.Preset, ct);
    }
}
