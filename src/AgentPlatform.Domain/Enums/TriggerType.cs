namespace AgentPlatform.Domain.Enums;

/// <summary>
/// 工作流触发器的类型。Webhook（外部回调）与 Schedule（定时）复用同一
/// <see cref="AgentPlatform.Domain.Aggregates.WorkflowTriggers.WorkflowTrigger"/> 聚合；
/// Chat 触发不在此枚举内，而以 <c>ConversationWorkflowBinding</c> 关联表表示（一个会话可绑多个工作流）。
/// <see cref="Chat"/> 值仅用于 <c>TriggerWorkflowCommand</c> 的信封/审计标记（Chat 触发无 WorkflowTrigger 实体），
/// 不会被持久化为 WorkflowTrigger 的 Type 列。
/// </summary>
public enum TriggerType
{
    /// <summary>Webhook 触发器：外部系统经匿名端点携带 token 调用。</summary>
    Webhook = 0,

    /// <summary>定时触发器：按 cron 表达式周期性自动运行。</summary>
    Schedule = 1,

    /// <summary>Chat 触发器：会话绑定后由用户显式触发（无 WorkflowTrigger 实体，仅作信封/审计标记）。</summary>
    Chat = 2
}
