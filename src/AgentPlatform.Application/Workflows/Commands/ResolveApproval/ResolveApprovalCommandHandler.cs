using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.ResolveApproval;

internal sealed class ResolveApprovalCommandHandler
    : IRequestHandler<ResolveApprovalCommand, WorkflowDetailResponse?>
{
    private readonly IHumanApprovalRepository _approvalRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrchestrationPrimitive _primitive;

    public ResolveApprovalCommandHandler(
        IHumanApprovalRepository approvalRepository,
        IWorkflowRepository workflowRepository,
        IUnitOfWork unitOfWork,
        IOrchestrationPrimitive primitive)
    {
        _approvalRepository = approvalRepository;
        _workflowRepository = workflowRepository;
        _unitOfWork = unitOfWork;
        _primitive = primitive;
    }

    public async Task<WorkflowDetailResponse?> Handle(ResolveApprovalCommand request, CancellationToken ct)
    {
        var approval = await _approvalRepository.GetByIdAsync(request.ApprovalId, ct);
        if (approval is null)
            return null; // 404，不披露存在性

        // 租户隔离：审批记录归属的租户必须与当前请求租户一致，且绑定到路径中的工作流。
        if (approval.TenantId != request.TenantId || approval.WorkflowId != request.WorkflowId)
            return null; // 404，避免跨租户/跨工作流枚举

        var workflow = await _workflowRepository.GetByIdAsync(approval.WorkflowId, ct);
        if (workflow is null)
            return null;

        // 幂等：审批已解析（非 Pending）时不再重复处理，直接返回当前工作流状态。
        if (approval.Status == HumanApprovalStatus.Pending)
        {
            if (request.Approved)
                approval.Approve(request.Input);
            else
                approval.Reject(request.Input); // 拒绝时 Input 作为拒绝原因

            // 将 UserInput 节点结果写回并标记为已完成（Completed），使续跑时该节点被跳过。
            // 必须置 Completed（而非 Failed）：Failed 会被 UserInput 执行器重新触发审批，造成重复。
            var uiNode = workflow.Nodes.FirstOrDefault(n =>
                n.Type == StepType.UserInput && n.Name == approval.NodeName);
            if (uiNode is not null)
            {
                var result = request.Approved
                    ? (request.Input ?? string.Empty)
                    : (request.Input ?? "Rejected");
                uiNode.SetResult(result);
            }

            _workflowRepository.Update(workflow);
            _approvalRepository.Update(approval);
            await _unitOfWork.SaveChangesAsync(ct);

            // 续跑：ResumeAsync 仅在 Paused 态有效，内部重新加载工作流（共享 DbContext 跟踪实例），
            // 跳过已 Completed 的 UserInput 节点并继续后续节点。
            if (workflow.CurrentState == WorkflowState.Paused)
                await _primitive.ResumeAsync(workflow.Id, ct);
        }

        var latest = await _workflowRepository.GetByIdAsync(workflow.Id, ct)
                     ?? workflow;
        return GetWorkflowQuery.ToDetailResponse(latest);
    }
}
