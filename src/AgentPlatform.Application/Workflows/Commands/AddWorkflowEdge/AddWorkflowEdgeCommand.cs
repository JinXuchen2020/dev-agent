using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.AddWorkflowEdge;

/// <summary>HTTP 请求体：在工作流两个节点间追加一条有向边。</summary>
public sealed record AddWorkflowEdgeRequest(
    Guid SourceNodeId,
    Guid TargetNodeId,
    string? Label);

/// <summary>增量追加边的命令，返回新建边（含服务端生成的 Id）。</summary>
public sealed record AddWorkflowEdgeCommand(
    Guid Id,
    AddWorkflowEdgeRequest Request,
    Guid TenantId) : ICommand<WorkflowEdgeResponse?>;

internal sealed class AddWorkflowEdgeCommandHandler
    : IRequestHandler<AddWorkflowEdgeCommand, WorkflowEdgeResponse?>
{
    private readonly IWorkflowRepository _repo;

    public AddWorkflowEdgeCommandHandler(IWorkflowRepository repo) => _repo = repo;

    public async Task<WorkflowEdgeResponse?> Handle(AddWorkflowEdgeCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; edits are not allowed until it finishes.");

        var edge = wf.AddEdge(
            request.Request.SourceNodeId,
            request.Request.TargetNodeId,
            request.Request.Label);
        _repo.Update(wf);

        return new WorkflowEdgeResponse(edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Label);
    }
}
