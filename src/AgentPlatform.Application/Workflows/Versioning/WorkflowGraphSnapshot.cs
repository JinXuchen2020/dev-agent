using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using System.Text.Json;

namespace AgentPlatform.Application.Workflows.Versioning;

/// <summary>
/// 工作流定义（context + nodes + edges）的可序列化快照，用于在 <see cref="WorkflowVersion"/> 中
/// 存储与回滚。使用 System.Text.Json；<see cref="StepType"/> 以数字存储。
/// </summary>
public sealed record WorkflowGraphSnapshot(
    string Context,
    IReadOnlyList<WorkflowVersionNode> Nodes,
    IReadOnlyList<WorkflowVersionEdge> Edges)
{
    /// <summary>Captures the current definition of a workflow.</summary>
    public static WorkflowGraphSnapshot FromWorkflow(Workflow wf)
    {
        var (nodes, edges) = wf.GetEffectiveGraph();
        return new WorkflowGraphSnapshot(
            wf.Context,
            nodes.Select(n => new WorkflowVersionNode(
                n.Id, n.Type, n.Name, n.PositionX, n.PositionY, n.ConfigJson, n.AssignedAgentId)).ToList(),
            edges.Select(e => new WorkflowVersionEdge(
                e.Id, e.SourceNodeId, e.TargetNodeId, e.Label)).ToList());
    }

    /// <summary>Serializes the snapshot to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Deserializes a snapshot from JSON, throwing on corruption.</summary>
    public static WorkflowGraphSnapshot FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkflowGraphSnapshot>(json)
                ?? throw new InvalidOperationException("Workflow version snapshot is corrupt.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Workflow version snapshot is corrupt.", ex);
        }
    }

    /// <summary>
    /// Produces the tuple arguments expected by <see cref="Workflow.ReplaceGraph"/>.
    /// Snapshot node ids are reused as TempIds so the restored graph keeps an identical structure
    /// (the engine remaps TempId → fresh Guid internally, preserving edge topology).
    /// </summary>
    public (List<(Guid TempId, StepType Type, string Name, double X, double Y, string? Config, Guid? AgentId)> Nodes,
            List<(Guid TempId, Guid SourceTempId, Guid TargetTempId, string? Label)> Edges) ToReplaceGraphArgs()
    {
        var nodes = Nodes.Select(n => (n.Id, n.Type, n.Name, n.X, n.Y, n.ConfigJson, n.AssignedAgentId)).ToList();
        var edges = Edges.Select(e => (e.Id, e.Source, e.Target, e.Label)).ToList();
        return (nodes, edges);
    }
}

/// <summary>A single node captured in a version snapshot.</summary>
public sealed record WorkflowVersionNode(
    Guid Id, StepType Type, string Name, double X, double Y, string? ConfigJson, Guid? AssignedAgentId);

/// <summary>A single edge captured in a version snapshot.</summary>
public sealed record WorkflowVersionEdge(Guid Id, Guid Source, Guid Target, string? Label);
