namespace AgentPlatform.Domain.Aggregates.HumanApprovals;

/// <summary>
/// 人工审批门（HITL）的审批状态。
/// </summary>
public enum HumanApprovalStatus
{
    /// <summary>等待人工处理（节点已暂停，工作流处于 Paused）。</summary>
    Pending = 0,

    /// <summary>已批准，提交的人工输入将回流到工作流并续跑。</summary>
    Approved = 1,

    /// <summary>已拒绝，拒绝原因将作为节点结果回流，工作流续跑（由下游自行处理）。</summary>
    Rejected = 2
}
