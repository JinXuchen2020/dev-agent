using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.RemoveWorkflowNode;

/// <summary>增量移除单个节点（及其关联边）。返回 true 表示已移除，false 表示工作流/节点不存在。</summary>
public sealed record RemoveWorkflowNodeCommand(
    Guid Id,
    Guid NodeId,
    Guid TenantId) : ICommand<bool>;

internal sealed class RemoveWorkflowNodeCommandHandler
    : IRequestHandler<RemoveWorkflowNodeCommand, bool>
{
    private readonly IWorkflowRepository _repo;

    public RemoveWorkflowNodeCommandHandler(IWorkflowRepository repo) => _repo = repo;

    public async Task<bool> Handle(RemoveWorkflowNodeCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return false; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; edits are not allowed until it finishes.");

        if (!wf.Nodes.Any(n => n.Id == request.NodeId))
            return false;

        wf.RemoveNode(request.NodeId);
        _repo.Update(wf);
        return true;
    }
}
