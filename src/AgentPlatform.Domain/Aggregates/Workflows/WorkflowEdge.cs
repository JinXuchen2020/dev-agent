namespace AgentPlatform.Domain.Aggregates.Workflows;

/// <summary>
/// 工作流 DAG 中的有向边，连接源节点与目标节点。
/// </summary>
public sealed class WorkflowEdge
{
    /// <summary>获取边的唯一标识符。</summary>
    public Guid Id { get; private init; }

    /// <summary>获取源节点的标识符。</summary>
    public Guid SourceNodeId { get; private init; }

    /// <summary>获取目标节点的标识符。</summary>
    public Guid TargetNodeId { get; private init; }

    /// <summary>获取可选的边标签。</summary>
    public string? Label { get; private init; }

    /// <summary>
    /// 初始化 <see cref="WorkflowEdge"/> 类的新实例。
    /// </summary>
    public WorkflowEdge(Guid id, Guid sourceNodeId, Guid targetNodeId, string? label)
    {
        Id = id;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        Label = label;
    }
}
