using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RunNode;

/// <summary>
/// Executes a single node of an existing workflow for debugging, without running or
/// completing the whole workflow. Does not implement <see cref="ICommand{T}"/> because the
/// node runner manages its own persistence.
/// </summary>
/// <param name="Id">The workflow identifier.</param>
/// <param name="NodeId">The node identifier to execute.</param>
/// <param name="TenantId">The tenant that owns the workflow (resolved by the controller).</param>
public record RunNodeCommand(Guid Id, Guid NodeId, Guid TenantId) : IRequest<WorkflowNodeRunResult?>;

internal sealed class RunNodeCommandHandler
    : IRequestHandler<RunNodeCommand, WorkflowNodeRunResult?>
{
    private readonly IWorkflowRepository _repo;
    private readonly IWorkflowNodeRunner _runner;

    public RunNodeCommandHandler(IWorkflowRepository repo, IWorkflowNodeRunner runner)
    {
        _repo = repo;
        _runner = runner;
    }

    public async Task<WorkflowNodeRunResult?> Handle(RunNodeCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running)
            throw new WorkflowConflictException($"Workflow '{wf.Id}' is already running; single-step is not allowed.");

        return await _runner.RunNodeAsync(wf, request.NodeId, ct);
    }
}
