using AgentPlatform.Application.Workflows.Queries.GetWorkflow;
using MediatR;

namespace AgentPlatform.Application.Workflows.Commands.ResolveApproval;

/// <summary>
/// 解析（批准/拒绝）一个 <see cref="AgentPlatform.Domain.Aggregates.HumanApprovals.HumanApproval"/>
/// 门（F20 S3 决策）。由 <see cref="AgentPlatform.Domain.Enums.StepType.UserInput"/> 节点暂停时创建，经此命令写回节点结果并续跑工作流。
/// 实现 <see cref="IRequest{T}"/>（而非 <see cref="Abstractions.ICommand{T}"/>），因为
/// <see cref="Abstractions.IOrchestrationPrimitive.ResumeAsync"/> 内部自行管理分步持久化；
/// 若经 UnitOfWorkBehavior 自动保存会与续跑的内部 SaveChanges 冲突（双写）。
/// </summary>
/// <param name="WorkflowId">关联工作流标识符（路径中的 {id}，用于一致性与租户校验）。</param>
/// <param name="ApprovalId">待解析审批记录的唯一标识符。</param>
/// <param name="Approved">true=批准（写入人工输入）；false=拒绝（写入拒绝原因）。</param>
/// <param name="Input">批准时的人工输入 / 拒绝时的原因（可为空）。</param>
/// <param name="TenantId">当前请求租户（解析于控制器，用于租户隔离校验）。</param>
public sealed record ResolveApprovalCommand(
    Guid WorkflowId,
    Guid ApprovalId,
    bool Approved,
    string? Input = null,
    Guid TenantId = default
) : IRequest<WorkflowDetailResponse?>;
