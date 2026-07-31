using AgentPlatform.Domain.Abstractions;
using AgentPlatform.Domain.Enums;

namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// 工作流 DAG 中的单个节点。携带其执行 <see cref="StepType"/>、画布坐标，
/// 以及节点专属配置（JSON）。
/// </summary>
public sealed class WorkflowNode : IWorkflowExecutable
{
    /// <summary>获取节点的唯一标识符。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取节点的执行类型（决定执行器路由）。</summary>
    public StepType Type { get; private set; }

    /// <summary>获取节点的可读名称（同时作为代理分配的键）。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>获取零基执行顺序（由图拓扑推导）。</summary>
    public int Order { get; private set; }

    /// <summary>获取画布 X 坐标。</summary>
    public double PositionX { get; private set; }

    /// <summary>获取画布 Y 坐标。</summary>
    public double PositionY { get; private set; }

    /// <summary>获取节点配置（JSON 形式：systemPrompt / agentId / criteria / …）。</summary>
    public string ConfigJson { get; private set; } = "{}";

    /// <summary>获取节点的当前执行状态。</summary>
    public WorkflowState State { get; private set; }

    /// <summary>获取节点完成时产生的结果（如有）。</summary>
    public string? Result { get; private set; }

    /// <summary>获取节点失败时记录的错误详情（如有）。</summary>
    public string? ErrorDetail { get; private set; }

    /// <summary>获取分配给该节点的代理标识符（如有）。</summary>
    public Guid? AssignedAgentId { get; private set; }

    /// <summary>获取节点最近更新的 UTC 时间戳。</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// 初始化 <see cref="WorkflowNode"/> 类的新实例。
    /// </summary>
    public WorkflowNode(Guid id, StepType type, string name, double positionX, double positionY, string? configJson, Guid? assignedAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Type = type;
        Name = name;
        PositionX = positionX;
        PositionY = positionY;
        ConfigJson = configJson ?? "{}";
        State = WorkflowState.Pending;
        AssignedAgentId = assignedAgentId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>重新设置节点顺序（在由图拓扑重新推导时调用）。</summary>
    public void SetOrder(int order) => Order = order;

    /// <summary>重命名节点。</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更改节点执行类型。</summary>
    public void SetType(StepType type)
    {
        Type = type;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更新画布坐标。</summary>
    public void UpdatePosition(double positionX, double positionY)
    {
        PositionX = positionX;
        PositionY = positionY;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>更新节点配置 JSON。</summary>
    public void UpdateConfig(string configJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configJson);
        ConfigJson = configJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>为节点分配代理。</summary>
    public void AssignAgent(Guid agentId)
    {
        AssignedAgentId = agentId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>设置节点的执行状态。</summary>
    public void SetState(WorkflowState state)
    {
        State = state;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>将节点重置为执行前状态（Pending，清除结果与错误），供工作流重跑。</summary>
    public void Reset()
    {
        State = WorkflowState.Pending;
        Result = null;
        ErrorDetail = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>设置结果并将节点标记为已完成。</summary>
    public void SetResult(string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        Result = result;
        State = WorkflowState.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>记录错误并将节点标记为失败。</summary>
    public void SetError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ErrorDetail = error;
        State = WorkflowState.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    StepType? IWorkflowExecutable.Type => Type;
}
