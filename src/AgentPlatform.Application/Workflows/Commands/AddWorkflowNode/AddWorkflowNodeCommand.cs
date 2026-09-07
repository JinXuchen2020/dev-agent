using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.AddWorkflowNode;

/// <summary>HTTP 请求体：向工作流追加单个节点。</summary>
public sealed record AddWorkflowNodeRequest(
    StepType Type,
    string Name,
    double PositionX,
    double PositionY,
    string? Config,
    Guid? AssignedAgentId);

/// <summary>增量追加节点的命令，返回新建节点（含服务端生成的 Id）。</summary>
public sealed record AddWorkflowNodeCommand(
    Guid Id,
    AddWorkflowNodeRequest Request,
    Guid TenantId) : ICommand<WorkflowNodeResponse?>;

internal sealed class AddWorkflowNodeCommandHandler
    : IRequestHandler<AddWorkflowNodeCommand, WorkflowNodeResponse?>
{
    private readonly IWorkflowRepository _repo;

    public AddWorkflowNodeCommandHandler(IWorkflowRepository repo) => _repo = repo;

    public async Task<WorkflowNodeResponse?> Handle(AddWorkflowNodeCommand request, CancellationToken ct)
    {
        var wf = await _repo.GetByIdAsync(request.Id, ct);
        if (wf is null || wf.TenantId != request.TenantId)
            return null; // 404, existence not disclosed

        if (wf.CurrentState is WorkflowState.Running or WorkflowState.Paused)
            throw new WorkflowConflictException(
                $"Workflow '{wf.Id}' is {wf.CurrentState}; edits are not allowed until it finishes.");

        // 增量编辑允许暂未通过 ValidateGraph 的局部图（运行端才会全量校验），故此处不调用 ValidateGraph。
        var node = wf.AddNode(
            request.Request.Type,
            request.Request.Name,
            request.Request.PositionX,
            request.Request.PositionY,
            request.Request.Config,
            request.Request.AssignedAgentId);
        _repo.Update(wf); // tracked entity; UnitOfWorkBehavior commits

        return new WorkflowNodeResponse(
            node.Id, node.Type, node.Name, node.Order, node.PositionX, node.PositionY,
            node.ConfigJson, node.State, node.Result, node.ErrorDetail, node.AssignedAgentId);
    }
}
