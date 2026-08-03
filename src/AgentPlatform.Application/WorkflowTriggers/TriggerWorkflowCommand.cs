using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Enums;
using MediatR;

namespace AgentPlatform.Application.WorkflowTriggers;

/// <summary>
/// 通过触发器运行一个已有工作流：注入触发器信封到共享 Context（运行时落入 Blackboard 供节点消费），
/// 并以 Sequential 预设启动编排。复用于 Webhook / Schedule / Chat 三类触发器。
/// 不实现 <see cref="ICommand{T}"/>（与 RunExistingWorkflow 一致）——编排器自身管理逐步持久化。
/// </summary>
/// <param name="WorkflowId">目标工作流标识。</param>
/// <param name="TenantId">工作流所属租户（调用方需先行鉴权，handler 内再次校验归属）。</param>
/// <param name="TriggerType">触发器类型（Webhook / Schedule / Chat）。</param>
/// <param name="PayloadJson">触发器载荷 JSON（Webhook 请求体 / Chat 消息 / 调度元数据）；可为 null。</param>
public record TriggerWorkflowCommand(
    Guid WorkflowId,
    Guid TenantId,
    TriggerType TriggerType,
    string? PayloadJson = null
) : IRequest<TriggerRunResult?>;

/// <summary>触发器运行结果（供匿名 Webhook 端点返回最小信息）。</summary>
public sealed record TriggerRunResult(Guid WorkflowId, string WorkflowName, WorkflowState State);
