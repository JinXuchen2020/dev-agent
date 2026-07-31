using AgentPlatform.Application.Workflows.Versioning;
using AgentPlatform.Domain.Aggregates.Workflows;
using AgentPlatform.Domain.Enums;
using Xunit;

namespace AgentPlatform.Application.Tests.Workflows;

/// <summary>Verifies the version snapshot serializes, deserializes, and restores an equivalent graph.</summary>
public sealed class WorkflowGraphSnapshotTests
{
    private static Workflow BuildDag(Guid tenantId)
    {
        var start = Guid.NewGuid();
        var llm = Guid.NewGuid();
        var end = Guid.NewGuid();
        var wf = new Workflow(Guid.NewGuid(), "Test WF", tenantId);
        wf.ReplaceGraph(
            new List<(Guid, StepType, string, double, double, string?, Guid?)>
            {
                (start, StepType.Start, "Start", 0, 0, "{}", null),
                (llm, StepType.LLM, "Step1", 0, 120, "{\"a\":1}", null),
                (end, StepType.End, "End", 0, 240, "{}", null),
            },
            new List<(Guid, Guid, Guid, string?)>
            {
                (Guid.NewGuid(), start, llm, null),
                (Guid.NewGuid(), llm, end, null),
            });
        return wf;
    }

    [Fact]
    public void Snapshot_RoundTrips_And_Restores_EquivalentGraph()
    {
        var wf = BuildDag(Guid.NewGuid());
        var snapshot = WorkflowGraphSnapshot.FromWorkflow(wf);
        var json = snapshot.ToJson();
        var restored = WorkflowGraphSnapshot.FromJson(json);
        var (nodes, edges) = restored.ToReplaceGraphArgs();

        var target = new Workflow(Guid.NewGuid(), "Restored", Guid.NewGuid());
        target.ReplaceGraph(nodes, edges);

        Assert.Equal(3, target.Nodes.Count);
        Assert.Contains(target.Nodes, n => n.Type == StepType.Start);
        Assert.Contains(target.Nodes, n => n.Type == StepType.End);
        Assert.Contains(target.Nodes, n =>
            n.Type == StepType.LLM && n.Name == "Step1" && n.ConfigJson == "{\"a\":1}");
        Assert.Equal(2, target.Edges.Count);
        Assert.Equal(wf.Context, target.Context);
    }

    [Fact]
    public void FromJson_Throws_OnCorruptPayload() =>
        Assert.Throws<InvalidOperationException>(() => WorkflowGraphSnapshot.FromJson("not-json"));
}
