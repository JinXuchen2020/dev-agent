using AgentPlatform.Domain;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>
/// 覆盖设计文档 §9 后端验收：<see cref="Workflow.ValidateGraph"/> / <see cref="Workflow.GetTopologicalOrder"/>
/// 以及图与遗留线性步骤投影的互转（roundtrip）。
/// </summary>
public sealed class WorkflowGraphTests
{
    private static Workflow NewWorkflow() => new(Guid.NewGuid(), "wf", Guid.NewGuid());

    /// <summary>通过 AddNode/AddEdge 构建未经验证的图（AddNode/AddEdge 不触发 ValidateGraph）。</summary>
    private static Workflow BuildUnvalidatedGraph(params (string Name, StepType Type)[] nodes)
    {
        var wf = NewWorkflow();
        foreach (var n in nodes)
            wf.AddNode(n.Type, n.Name, 0, 0, null, null);
        return wf;
    }

    /// <summary>通过 ReplaceGraph 构建已验证的线性链 Start → … → End。</summary>
    private static Workflow BuildValidChain(params (string Name, StepType Type)[] nodes)
    {
        var wf = NewWorkflow();
        var tempIds = nodes.Select(_ => Guid.NewGuid()).ToArray();
        var nodeTuples = nodes
            .Select((n, i) => (tempIds[i], n.Type, n.Name, (double)(i * 100), 0d, (string?)null, (Guid?)null))
            .ToList();
        var edgeTuples = new List<(Guid, Guid, Guid, string?)>();
        for (var i = 0; i < tempIds.Length - 1; i++)
            edgeTuples.Add((Guid.NewGuid(), tempIds[i], tempIds[i + 1], null));
        wf.ReplaceGraph(nodeTuples, edgeTuples);
        return wf;
    }

    // ──────────────────────────────────────────────
    // ValidateGraph：结构非法 → 抛 WorkflowGraphException
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidateGraph_NoEndNode_Throws()
    {
        var wf = BuildUnvalidatedGraph(("Start", StepType.Start), ("A", StepType.LLM));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);

        Assert.Throws<WorkflowGraphException>(() => wf.ValidateGraph());
    }

    [Fact]
    public void ValidateGraph_MultipleStartNodes_Throws()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start1", StepType.Start),
            ("Start2", StepType.Start),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[2].Id, null);

        Assert.Throws<WorkflowGraphException>(() => wf.ValidateGraph());
    }

    [Fact]
    public void ValidateGraph_Cycle_Throws()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null); // Start→A
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null); // A→B
        wf.AddEdge(wf.Nodes[2].Id, wf.Nodes[1].Id, null); // B→A（环）
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[3].Id, null); // A→End

        Assert.Throws<WorkflowGraphException>(() => wf.ValidateGraph());
    }

    [Fact]
    public void ValidateGraph_DisconnectedNode_Throws()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("End", StepType.End),
            ("Orphan", StepType.LLM));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null);

        Assert.Throws<WorkflowGraphException>(() => wf.ValidateGraph());
    }

    [Fact]
    public void ValidateGraph_ValidLinearDag_DoesNotThrow()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null);

        wf.ValidateGraph(); // 不抛异常
    }

    // ──────────────────────────────────────────────
    // GetTopologicalOrder：拓扑序正确
    // ──────────────────────────────────────────────

    [Fact]
    public void GetTopologicalOrder_LinearChain_StartToEnd()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null);
        wf.AddEdge(wf.Nodes[2].Id, wf.Nodes[3].Id, null);

        var order = wf.GetTopologicalOrder().Select(n => n.Name).ToList();

        Assert.Equal(["Start", "A", "B", "End"], order);
    }

    [Fact]
    public void GetTopologicalOrder_Diamond_RespectsPartialOrder()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.LLM),
            ("C", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null); // Start→A
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null); // A→B
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[3].Id, null); // A→C
        wf.AddEdge(wf.Nodes[2].Id, wf.Nodes[4].Id, null); // B→End
        wf.AddEdge(wf.Nodes[3].Id, wf.Nodes[4].Id, null); // C→End

        var order = wf.GetTopologicalOrder().Select(n => n.Name).ToList();

        Assert.Equal("Start", order[0]);
        Assert.Equal("End", order[^1]);
        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("A") < order.IndexOf("C"));
        Assert.True(order.IndexOf("B") < order.IndexOf("End"));
        Assert.True(order.IndexOf("C") < order.IndexOf("End"));
    }

    [Fact]
    public void GetTopologicalOrder_MultiBranch_StartsAndEndsCorrectly()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[2].Id, null);
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[3].Id, null);
        wf.AddEdge(wf.Nodes[2].Id, wf.Nodes[3].Id, null);

        var order = wf.GetTopologicalOrder().Select(n => n.Name).ToList();

        Assert.Equal("Start", order[0]);
        Assert.Equal("End", order[^1]);
        Assert.Contains("A", order);
        Assert.Contains("B", order);
    }

    [Fact]
    public void GetTopologicalOrder_Cycle_Throws()
    {
        var wf = BuildUnvalidatedGraph(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.LLM),
            ("End", StepType.End));
        wf.AddEdge(wf.Nodes[0].Id, wf.Nodes[1].Id, null);
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[2].Id, null);
        wf.AddEdge(wf.Nodes[2].Id, wf.Nodes[1].Id, null); // 环
        wf.AddEdge(wf.Nodes[1].Id, wf.Nodes[3].Id, null);

        Assert.Throws<WorkflowGraphException>(() => wf.GetTopologicalOrder());
    }

    // ──────────────────────────────────────────────
    // Roundtrip：图 ↔ 遗留线性步骤投影
    // ──────────────────────────────────────────────

    [Fact]
    public void ReplaceGraph_SyncsLegacySteps_ExcludingStartEnd()
    {
        var wf = BuildValidChain(
            ("Start", StepType.Start),
            ("A", StepType.LLM),
            ("B", StepType.Agent),
            ("End", StepType.End));

        Assert.True(wf.IsDag);
        // 步骤投影排除 Start/End 标记节点 → 仅 A、B
        Assert.Equal(2, wf.Steps.Count);
        Assert.Contains(wf.Steps, s => s.StepName == "A");
        Assert.Contains(wf.Steps, s => s.StepName == "B");
    }

    [Fact]
    public void ReplaceSteps_BuildsLinearDag_AndProjectsSteps()
    {
        var wf = NewWorkflow();
        wf.ReplaceSteps(["a", "b"]);

        Assert.False(wf.IsDag);
        // ReplaceSteps 合成 Start → a → b → End 链；步骤投影 = a、b
        Assert.Equal(2, wf.Steps.Count);
        Assert.Equal("a", wf.Steps[0].StepName);
        Assert.Equal("b", wf.Steps[1].StepName);
        // 合成 DAG 含 4 个节点（Start、a、b、End）
        Assert.Equal(4, wf.Nodes.Count);
    }

    [Fact]
    public void ReplaceGraph_ThenLegacyStepsRoundtrip_PreservesNames()
    {
        var wf = BuildValidChain(
            ("Start", StepType.Start),
            ("Research", StepType.LLM),
            ("Critique", StepType.Critic),
            ("End", StepType.End));

        Assert.Equal(2, wf.Steps.Count);
        Assert.Equal("Research", wf.Steps[0].StepName);
        Assert.Equal("Critique", wf.Steps[1].StepName);
    }
}
