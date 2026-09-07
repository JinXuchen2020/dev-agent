using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RemoveWorkflowEdge;

/// <summary>增量移除单条边。返回 true 表示已移除，false 表示工作流/边不存在。</summary>
public sealed record RemoveWorkflowEdgeCommand(
    Guid Id,
    Guid EdgeId,
    Guid TenantId) : ICommand<bool>;

internal sealed class RemoveWorkflowEdgeCommandHandler
    : IRequestHandler<RemoveWorkflowEdgeCommand, bool>
{
    private readonly IWorkflowRepository _repo;

    public RemoveWorkflowEdgeCommandHandler(IWorkflowRepository repo) => _repo = repo;

    public async Task<bool> Handle(RemoveWorkflowEdgeCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return false; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; edits are not allowed until it finishes.");

        if (!wf.Edges.Any(e => e.Id == request.EdgeId))
            return false;

        wf.RemoveEdge(request.EdgeId);
        _repo.Update(wf);
        return true;
    }
}
