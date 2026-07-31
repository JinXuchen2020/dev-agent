using AgentPlatform.Domain.Abstractions;

namespace AgentPlatform.Domain.Aggregates.HumanApprovals;

/// <summary>
/// 人工审批门（HITL）聚合（F20 S3 决策）。
/// 由 <see cref="StepType.UserInput"/> 节点执行器在暂停时创建（Pending），
/// 经专门的审批恢复端点解析（Approved/Rejected）后，节点结果被写回并续跑工作流。
/// 实体遵循租户隔离（<see cref="ITenantScoped"/>），由 AppDbContext 的查询过滤器强制。
/// </summary>
public sealed class HumanApproval : ITenantScoped
{
    /// <summary>获取审批记录的唯一标识符（由调用方以 ValueGeneratedNever 显式提供）。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取拥有该审批记录的租户标识符（租户隔离键）。</summary>
    public Guid TenantId { get; private init; }

    /// <summary>获取关联工作流的标识符。</summary>
    public Guid WorkflowId { get; private init; }

    /// <summary>获取触发审批的 UserInput 节点名称（同一工作流内定位用）。</summary>
    public string NodeName { get; private init; } = null!;

    /// <summary>获取展示给审批人的提示语。</summary>
    public string Prompt { get; private init; } = null!;

    /// <summary>获取当前审批状态。</summary>
    public HumanApprovalStatus Status { get; private set; }

    /// <summary>获取审批人提交的人工输入或拒绝原因（解析后填充）。</summary>
    public string? SubmittedInput { get; private set; }

    /// <summary>获取审批解析（Approved/Rejected）的 UTC 时间。</summary>
    public DateTime? ResolvedAt { get; private set; }

    /// <summary>获取审批记录创建的 UTC 时间。</summary>
    public DateTime CreatedAt { get; private init; }

    /// <summary>
    /// 获取关联的 ExecutionLog 标识符（最佳努力填充；解析流程不依赖它，
    /// 仅用于审计关联。ExecutionLog 由 WorkflowStarted 领域事件异步创建，执行器未必能同步取到）。
    /// </summary>
    public Guid? ExecutionId { get; private set; }

    private HumanApproval() { }

    /// <summary>
    /// 初始化一个处于 <see cref="HumanApprovalStatus.Pending"/> 的审批记录。
    /// </summary>
    public HumanApproval(
        Guid id,
        Guid tenantId,
        Guid workflowId,
        string nodeName,
        string prompt,
        Guid? executionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        Id = id;
        TenantId = tenantId;
        WorkflowId = workflowId;
        NodeName = nodeName;
        Prompt = prompt;
        Status = HumanApprovalStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        ExecutionId = executionId;
    }

    /// <summary>批准审批，记录提交的人工输入。</summary>
    public void Approve(string? input)
    {
        if (Status != HumanApprovalStatus.Pending)
            throw new InvalidOperationException($"审批 {Id} 非待处理状态（当前：{Status}），无法批准。");
        Status = HumanApprovalStatus.Approved;
        SubmittedInput = input;
        ResolvedAt = DateTime.UtcNow;
    }

    /// <summary>拒绝审批，记录拒绝原因。</summary>
    public void Reject(string? reason)
    {
        if (Status != HumanApprovalStatus.Pending)
            throw new InvalidOperationException($"审批 {Id} 非待处理状态（当前：{Status}），无法拒绝。");
        Status = HumanApprovalStatus.Rejected;
        SubmittedInput = reason;
        ResolvedAt = DateTime.UtcNow;
    }
}
