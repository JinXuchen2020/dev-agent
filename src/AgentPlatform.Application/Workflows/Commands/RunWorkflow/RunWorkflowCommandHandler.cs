using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunWorkflow;

internal sealed class RunWorkflowCommandHandler : IRequestHandler<RunWorkflowCommand, Workflow>
{
    private readonly IWorkflowRepository _repository;
    private readonly IWorkflowEngine _workflowEngine;

    public RunWorkflowCommandHandler(
        IWorkflowRepository repository,
        IWorkflowEngine workflowEngine)
    {
        _repository = repository;
        _workflowEngine = workflowEngine;
    }

    public async Task<Workflow> Handle(RunWorkflowCommand request, CancellationToken ct)
    {
        var workflow = new Workflow(Guid.NewGuid(), request.Name, request.TenantId);
        workflow.UpdateContext(request.InitialContext);

        _repository.Add(workflow);
        await _workflowEngine.StartAsync(workflow, ct);

        return workflow;
    }
}
