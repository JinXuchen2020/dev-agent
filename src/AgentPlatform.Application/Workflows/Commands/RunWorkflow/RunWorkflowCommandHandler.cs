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

        // Create steps from the request if provided (Blueprint C.2: sequential preset)
        if (request.Steps is { Count: > 0 })
        {
            for (var i = 0; i < request.Steps.Count; i++)
            {
                workflow.AddStep(new WorkflowStep(Guid.NewGuid(), i, request.Steps[i]));
            }
        }

        // The orchestration primitive handles per-step persistence internally
        return await _primitive.RunAsync(workflow, request.Preset, ct);
    }
}
