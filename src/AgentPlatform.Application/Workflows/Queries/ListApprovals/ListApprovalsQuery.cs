using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.HumanApprovals;
using AgentPlatform.Domain.Repositories;
using MediatR;

namespace AgentPlatform.Application.Workflows.Queries.ListApprovals;

/// <summary>
/// 列出某租户某工作流的全部人工审批门（HITL）记录（按创建时间倒序）。
/// 用于前端在暂停/运行中工作流上展示待处理与已解析的审批。
/// </summary>
/// <param name="WorkflowId">工作流标识符（路径中的 {id}，用于租户隔离校验）。</param>
public sealed record ListApprovalsQuery(Guid WorkflowId) : IRequest<IReadOnlyList<HumanApprovalDto>>;

/// <summary>人工审批门记录的只读投影（不含租户敏感信息）。</summary>
public sealed record HumanApprovalDto(
    Guid Id,
    Guid WorkflowId,
    string NodeName,
    string Prompt,
    HumanApprovalStatus Status,
    string? SubmittedInput,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    Guid? ExecutionId);

internal sealed class ListApprovalsQueryHandler(
    IHumanApprovalRepository approvalRepository,
    IWorkflowRepository workflowRepository,
    ITenantProvider tenantProvider)
    : IRequestHandler<ListApprovalsQuery, IReadOnlyList<HumanApprovalDto>>
{
    public async Task<IReadOnlyList<HumanApprovalDto>> Handle(ListApprovalsQuery request, CancellationToken ct)
    {
        var tenantId = tenantProvider.GetTenantId();

        // 工作流存在性 + 租户归属校验（避免跨租户枚举）。
        var workflow = await workflowRepository.GetByIdAsync(request.WorkflowId, ct);
        if (workflow is null || workflow.TenantId != tenantId)
            return [];

        var approvals = await approvalRepository.GetByWorkflowAsync(tenantId, request.WorkflowId, ct);
        return approvals.Select(a => new HumanApprovalDto(
            a.Id, a.WorkflowId, a.NodeName, a.Prompt, a.Status,
            a.SubmittedInput, a.ResolvedAt, a.CreatedAt, a.ExecutionId)).ToList();
    }
}
